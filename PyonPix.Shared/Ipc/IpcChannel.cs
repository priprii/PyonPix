using System;
using System.IO.MemoryMappedFiles;

namespace PyonPix.Shared.Ipc;

public sealed class IpcChannel : IDisposable {
    // Header:
    // 0  : int    Magic
    // 4  : int    SlotCount
    // 8  : int    SlotPayloadSize
    // 12 : long   WriteSeq
    // 20 : long   ReadSeq
    private const int Magic = 0x31585049; // "IPX1"
    private const int HeaderSize = 32;

    private const int OffsetMagic = 0;
    private const int OffsetSlotCount = 4;
    private const int OffsetSlotPayloadSize = 8;
    private const int OffsetWriteSeq = 12;
    private const int OffsetReadSeq = 20;

    // Slot:
    // 0  : long   Seq
    // 8  : int    Length
    // 12 : byte[] Payload
    private const int SlotSeqOffset = 0;
    private const int SlotLengthOffset = sizeof(long);
    private const int SlotHeaderSize = sizeof(long) + sizeof(int);

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly object _writeLock = new();

    public int SlotCount { get; }
    public int SlotPayloadSize { get; }
    private int SlotSize => SlotHeaderSize + SlotPayloadSize;
    public int Capacity => HeaderSize + (SlotCount * SlotSize);

    public IpcChannel(string name, int slotCount = 64, int slotPayloadSize = 64 * 1024) {
        if(slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        if(slotPayloadSize <= 0) throw new ArgumentOutOfRangeException(nameof(slotPayloadSize));

        SlotCount = slotCount;
        SlotPayloadSize = slotPayloadSize;

        _mmf = MemoryMappedFile.CreateOrOpen(name, Capacity, MemoryMappedFileAccess.ReadWrite);
        _view = _mmf.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);

        InitializeHeader();
    }

    private void InitializeHeader() {
        var magic = _view.ReadInt32(OffsetMagic);
        var count = _view.ReadInt32(OffsetSlotCount);
        var size = _view.ReadInt32(OffsetSlotPayloadSize);

        if(magic == Magic && count == SlotCount && size == SlotPayloadSize)
            return;

        _view.Write(OffsetMagic, Magic);
        _view.Write(OffsetSlotCount, SlotCount);
        _view.Write(OffsetSlotPayloadSize, SlotPayloadSize);
        _view.Write(OffsetWriteSeq, 0L);
        _view.Write(OffsetReadSeq, 0L);

        for(int i = 0; i < SlotCount; i++) {
            var slotOffset = GetSlotOffset(i);
            _view.Write(slotOffset + SlotSeqOffset, 0L);
            _view.Write(slotOffset + SlotLengthOffset, 0);
        }

        _view.Flush();
    }

    private int GetSlotOffset(long seq) {
        var slotIndex = (int)(seq % SlotCount);
        return HeaderSize + (slotIndex * SlotSize);
    }

    private long ReadWriteSeq() => _view.ReadInt64(OffsetWriteSeq);
    private long ReadReadSeq() => _view.ReadInt64(OffsetReadSeq);
    private void WriteWriteSeq(long value) => _view.Write(OffsetWriteSeq, value);
    private void WriteReadSeq(long value) => _view.Write(OffsetReadSeq, value);

    public void Write(ReadOnlySpan<byte> data) {
        if(data.Length > SlotPayloadSize)
            throw new InvalidOperationException($"IPC payload too large: {data.Length} > {SlotPayloadSize}.");

        lock(_writeLock) {
            long writeSeq = ReadWriteSeq();
            long readSeq = ReadReadSeq();

            var spin = new SpinWait();
            while(writeSeq - readSeq >= SlotCount) {
                spin.SpinOnce();
                readSeq = ReadReadSeq();
            }

            long nextSeq = writeSeq + 1;
            int slotOffset = GetSlotOffset(nextSeq);

            if(data.Length > 0) {
                var buffer = data.ToArray();
                _view.WriteArray(slotOffset + SlotHeaderSize, buffer, 0, buffer.Length);
            }

            _view.Write(slotOffset + SlotLengthOffset, data.Length);
            _view.Write(slotOffset + SlotSeqOffset, nextSeq);

            WriteWriteSeq(nextSeq);
            _view.Flush();
        }
    }

    public bool TryRead(out byte[] data) {
        data = Array.Empty<byte>();

        long writeSeq = ReadWriteSeq();
        long readSeq = ReadReadSeq();

        if(readSeq >= writeSeq)
            return false;

        long nextSeq = readSeq + 1;
        int slotOffset = GetSlotOffset(nextSeq);

        long slotSeq = _view.ReadInt64(slotOffset + SlotSeqOffset);
        if(slotSeq != nextSeq)
            return false;

        int length = _view.ReadInt32(slotOffset + SlotLengthOffset);
        if(length < 0 || length > SlotPayloadSize)
            throw new InvalidDataException($"Corrupt IPC payload length: {length}");

        data = new byte[length];
        if(length > 0)
            _view.ReadArray(slotOffset + SlotHeaderSize, data, 0, length);

        WriteReadSeq(nextSeq);
        _view.Flush();

        return true;
    }

    public void Dispose() {
        _view.Dispose();
        _mmf.Dispose();
    }
}
