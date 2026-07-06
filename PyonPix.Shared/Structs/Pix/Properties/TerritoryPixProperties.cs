using PyonPix.Shared.Structs.Territory;

namespace PyonPix.Shared.Structs.Pix.Properties;

public class TerritoryPixProperties : ILocal<SyncedTerritoryPixProperties> { // : IEquatable<TerritoryProperties> {
    public ushort WorldId;
    public uint TerritoryId;
    public short Ward;
    public short Plot;
    public short Room;
    public Floor Floor;
    public bool Persistent = true;

    public TerritoryPixProperties() { }

    public TerritoryPixProperties(ushort worldId, uint territoryId, short ward, short plot, short room, Floor floor) {
        WorldId = worldId;
        TerritoryId = territoryId;
        Ward = ward;
        Plot = plot;
        Room = room;
        Floor = floor;
    }

    public override string ToString() => $"{WorldId}:{TerritoryId}:{Ward}:{Plot}:{Room}:{(ushort)Floor}";

    public bool Matches(TerritoryData? other, bool persistent) {
        if(other == null) return false;
        if(WorldId != other.WorldId) return false;
        if(TerritoryId != other.TerritoryId) return false;
        if(Ward != other.Ward) return false;
        if(Room != other.Room) return false;

        if(Floor != Floor.None) { // in house, plot must match, floor can persist
            if(Plot != other.Plot) return false;
            if(!persistent && Floor != other.Floor) return false;
        } else { // not in house, plot can persist, floor must be None
            if(!persistent && Plot != other.Plot) return false;
            if(Floor != other.Floor) return false;
        }

        return true;
    }

    public SyncedTerritoryPixProperties ToSynced() {
        return new SyncedTerritoryPixProperties {
            WorldId = (short)WorldId,
            TerritoryId = (short)TerritoryId,
            Ward = Ward,
            Plot = Plot,
            Room = Room,
            Floor = (short)Floor,
            Persistent = Persistent
        };
    }
}

public class SyncedTerritoryPixProperties : ISynced<TerritoryPixProperties> {
    public short WorldId { get; set; }
    public short TerritoryId { get; set; }
    public short Ward { get; set; }
    public short Plot { get; set; }
    public short Room { get; set; }
    public short Floor { get; set; }
    public bool Persistent { get; set; }

    public void ApplyTo(TerritoryPixProperties target) {
        target.WorldId = (ushort)WorldId;
        target.TerritoryId = (uint)TerritoryId;
        target.Ward = Ward;
        target.Plot = Plot;
        target.Room = Room;
        target.Floor = (Floor)Floor;
        target.Persistent = Persistent;
    }
}
