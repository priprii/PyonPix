using PyonPix.Shared.Sync.Dto.Client;

namespace PyonPix.Shared.Structs.Territory;

public class TerritoryData : IEquatable<TerritoryData> {
    public ushort WorldId;
    public uint TerritoryId;
    public short Ward;
    public short Plot;
    public short Room;
    public Floor Floor;

    public string WorldName = string.Empty;
    public string TerritoryName = string.Empty;
    public string TerritorySubName = string.Empty;

    public uint RawTerritoryId;

    public TerritoryData() { }

    public TerritoryData(TerritoryData other) {
        WorldId = other.WorldId;
        TerritoryId = other.TerritoryId;
        Ward = other.Ward;
        Plot = other.Plot;
        Room = other.Room;
        Floor = other.Floor;
        WorldName = other.WorldName;
        TerritoryName = other.TerritoryName;
        TerritorySubName = other.TerritorySubName;
        RawTerritoryId = other.RawTerritoryId;
    }

    public override string ToString() => $"{WorldId}:{TerritoryId}:{Ward}:{Plot}:{Room}:{(ushort)Floor}";

    public TerritoryDto ToDto() => new TerritoryDto((short)WorldId, (short)TerritoryId, Ward, Plot, Room);

    public static TerritoryData Parse(string value) {
        var parts = value.Split(':');
        return new TerritoryData {
            WorldId = parts.Length < 1 ? (ushort)0 : ushort.Parse(parts[0]),
            TerritoryId = parts.Length < 2 ? 0 : uint.Parse(parts[1]),
            Ward = parts.Length < 3 ? (short)0 : short.Parse(parts[2]),
            Plot = parts.Length < 4 ? (short)0 : short.Parse(parts[3]),
            Room = parts.Length < 5 ? (short)0 : short.Parse(parts[4]),
            Floor = parts.Length < 6 ? Floor.None : (Floor)ushort.Parse(parts[5])
        };
    }

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

    public bool MatchesWTWP(TerritoryData? other) {
        if(other == null) return false;
        if(WorldId != other.WorldId) return false;
        if(TerritoryId != other.TerritoryId) return false;
        if(Ward != other.Ward) return false;
        if(Plot != other.Plot) return false;

        return true;
    }

    public bool Equals(TerritoryData? other) {
        if(ReferenceEquals(null, other)) return false;
        if(ReferenceEquals(this, other)) return true;
        return WorldId == other.WorldId
            && TerritoryId == other.TerritoryId
            && Ward == other.Ward
            && Plot == other.Plot
            && Room == other.Room
            && Floor == other.Floor;
    }
    public override bool Equals(object? obj) => Equals(obj as TerritoryData);
    public override int GetHashCode() => HashCode.Combine(WorldId, TerritoryId, Ward, Plot, Room, (ushort)Floor);
    public static bool operator ==(TerritoryData? left, TerritoryData? right) => Equals(left, right);
    public static bool operator !=(TerritoryData? left, TerritoryData? right) => !Equals(left, right);
}
