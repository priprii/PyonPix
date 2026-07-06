using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PyonPix.Config;
using PyonPix.Shared.Structs.Pix.Properties;
using PyonPix.Shared.Structs.Territory;
using PyonPix.Structs.PlayerState;

namespace PyonPix.Services.Game;

public enum Region {
    JP = 1,
    US = 2,
    EU = 3,
    OCE = 4
}

public class StateService(Configuration config, IServiceContext services) : BaseService(config, services) {
    public Vector3 LocalPlayerPosition { get; private set; }
    public Quaternion LocalPlayerRotation { get; private set; }
    public long LocalPlayerContentId { get; private set; }

    public bool LocalPlayerExists { get; private set; }

    private unsafe short CurrentWard => (short)(HousingManager.Instance()->GetCurrentWard() + 1);
    private unsafe short CurrentPlot => (short)(HousingManager.Instance()->GetCurrentPlot() + 1);
    private unsafe short CurrentRoom => HousingManager.Instance()->GetCurrentRoom();
    private unsafe Floor CurrentFloor { 
        get {
            if(!IsInPlotInside) return Floor.None;
            var floor = HousingManager.Instance()->IndoorTerritory->CurrentFloor;
            return floor == 10 ? Floor.Basement : floor == 0 ? Floor.Ground : floor == 1 ? Floor.Top : Floor.None;
        } 
    }

    public bool IsInWard => CurrentWard > 0;
    public bool IsInPlot => CurrentPlot > 0;
    public bool IsInRoom => CurrentRoom > 0;

    public unsafe bool IsInside => HousingManager.Instance()->IsInside();
    public unsafe bool IsOutside => HousingManager.Instance()->IsOutside();
    public unsafe bool IsInWorkshop => HousingManager.Instance()->IsInWorkshop();

    public bool IsInWardArea => IsInWard && !IsInPlot && IsOutside; // In ward region, but not in a garden
    public bool IsInPlotOutside => IsInWard && IsInPlot && IsOutside; // In house garden
    public bool IsInPlotInside => IsInWard && IsInPlot && !IsInRoom && IsInside; // In house, but not in fc room/apt room
    public bool IsInFCRoom => IsInWard && IsInPlot && IsInRoom && IsInside;
    public bool IsInAptRoom => IsInWard && !IsInPlot && IsInRoom && IsInside;
    public bool IsInAptLobby => IsInWard && !IsInPlot && !IsInRoom && IsInside;
    public bool IsInNonResidentialArea => !IsInWard && !IsInPlot && !IsInRoom && !IsInside && !IsOutside && !IsInWorkshop;

    public ExcelSheet<World> WorldSheet { get; private set; } = null!;
    public ExcelSheet<TerritoryType> TerritorySheet { get; private set; } = null!;

    public Dictionary<Region, List<WorldInfo>> Worlds = [];
    public List<ResidentialTerritory> ResidentialTerritories = [];
    public List<NonResidentialTerritory> NonResidentialTerritories = [];
    public readonly List<(uint Id, string Name, bool IsResidential)> UITerritoryList = [];

    public TerritoryData? CurrentTerritory;
    private TerritoryData? PreviousTerritory = null;

    public event Action<bool, bool, TerritoryData?>? TerritoryChanged;
    public event Action<TerritoryData?>? TerritoryLoaded;
    private bool IsLoadingTerritory;

    private bool IsInitialLoad = true;
    public event Action<TerritoryData?>? InitialLoad;

    public override Task Initialize() {
        WorldSheet = Services.DataManager.GetExcelSheet<World>()!;
        TerritorySheet = Services.DataManager.GetExcelSheet<TerritoryType>()!;
        InitializeWorlds();
        InitializeTerritories();
        BuildUITerritoryList(true);
        Services.ClientState.TerritoryChanged += (territoryId) => { EnsureDespawn(); };
        return Task.CompletedTask;
    }

    private void InitializeWorlds() {
        foreach(var world in WorldSheet) {
            uint? r = world.DataCenter.ValueNullable?.Region.RowId;
            if(!world.IsPublic || r < 1 || r > 4) continue;
            if(world.Name.IsEmpty || world.Name.ToString().Contains('-')) continue;
            var id = (ushort)world.RowId;
            var region = (Region)r!;
            if(!Worlds.ContainsKey(region)) Worlds.Add(region, []);
            Worlds[region].Add(new WorldInfo(id, world.Name.ToString()));
        }
        foreach(var kvp in Worlds) {
            kvp.Value.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }
    }

    private void InitializeTerritories() {
        // not ideal but better for presentation
        ResidentialTerritories = [
            new(136, "Mist", "", ResidentialType.Ward),
            new(282, "Mist", "Private Cottage", ResidentialType.House),
            new(283, "Mist", "Private House", ResidentialType.House),
            new(284, "Mist", "Private Mansion", ResidentialType.House),
            new(384, "Mist", "Private Chambers", ResidentialType.Chambers),
            new(423, "Mist", "Company Workshop", ResidentialType.Workshop),
            new(573, "Mist", "Apartment Lobby", ResidentialType.ApartmentLobby),
            new(608, "Mist", "Apartment", ResidentialType.Apartment),
            new(340, "Lavender Beds", "", ResidentialType.Ward),
            new(342, "Lavender Beds", "Private Cottage", ResidentialType.House),
            new(343, "Lavender Beds", "Private House", ResidentialType.House),
            new(344, "Lavender Beds", "Private Mansion", ResidentialType.House),
            new(385, "Lavender Beds", "Private Chambers", ResidentialType.Chambers),
            new(425, "Lavender Beds", "Company Workshop", ResidentialType.Workshop),
            new(574, "Lavender Beds", "Apartment Lobby", ResidentialType.ApartmentLobby),
            new(609, "Lavender Beds", "Apartment", ResidentialType.Apartment),
            new(341, "Goblet", "", ResidentialType.Ward),
            new(345, "Goblet", "Private Cottage", ResidentialType.House),
            new(346, "Goblet", "Private House", ResidentialType.House),
            new(347, "Goblet", "Private Mansion", ResidentialType.House),
            new(386, "Goblet", "Private Chambers", ResidentialType.Chambers),
            new(424, "Goblet", "Company Workshop", ResidentialType.Workshop),
            new(575, "Goblet", "Apartment Lobby", ResidentialType.ApartmentLobby),
            new(610, "Goblet", "Apartment", ResidentialType.Apartment),
            new(641, "Shirogane", "", ResidentialType.Ward),
            new(649, "Shirogane", "Private Cottage", ResidentialType.House),
            new(650, "Shirogane", "Private House", ResidentialType.House),
            new(651, "Shirogane", "Private Mansion", ResidentialType.House),
            new(652, "Shirogane", "Private Chambers", ResidentialType.Chambers),
            new(653, "Shirogane", "Company Workshop", ResidentialType.Workshop),
            new(654, "Shirogane", "Apartment Lobby", ResidentialType.ApartmentLobby),
            new(655, "Shirogane", "Apartment", ResidentialType.Apartment),
            new(979, "Empyreum", "", ResidentialType.Ward),
            new(980, "Empyreum", "Private Cottage", ResidentialType.House),
            new(981, "Empyreum", "Private House", ResidentialType.House),
            new(982, "Empyreum", "Private Mansion", ResidentialType.House),
            new(983, "Empyreum", "Private Chambers", ResidentialType.Chambers),
            new(984, "Empyreum", "Company Workshop", ResidentialType.Workshop),
            new(985, "Empyreum", "Apartment Lobby", ResidentialType.ApartmentLobby),
            new(999, "Empyreum", "Apartment", ResidentialType.Apartment)
        ];

        foreach(var t in TerritorySheet) {
            var placeName = t.PlaceName.ValueNullable?.Name.ExtractText();
            if(string.IsNullOrWhiteSpace(placeName)) continue;
            if(ResidentialTerritories.FirstOrDefault(x => x.Id == t.RowId) != default) continue;

            NonResidentialTerritories.Add(new((ushort)t.RowId, placeName));
        }
        NonResidentialTerritories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    public void BuildUITerritoryList(bool residentialOnly) {
        UITerritoryList.Clear();

        foreach(var r in ResidentialTerritories) {
            UITerritoryList.Add((r.Id, string.IsNullOrEmpty(r.SubName) ? r.Name : $"{r.Name} - {r.SubName}", true));
        }

        if(!residentialOnly) {
            foreach(var n in NonResidentialTerritories.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) {
                UITerritoryList.Add((n.Id, n.Name, false));
            }
        }
    }

    public unsafe void Update() {
        if(!Services.ClientState.IsLoggedIn || !Services.PlayerState.IsLoaded || Services.Condition[ConditionFlag.BetweenAreas]) { EnsureDespawn(); return; }
        var localPlayer = Services.Objects.LocalPlayer;
        if(localPlayer == null) { EnsureDespawn(); return; }
        var c = (Character*)localPlayer.Address;
        if(c == null || c->DrawObject == null) return;
        var cId = (long)Services.PlayerState.ContentId;
        if(cId == 0) return;
        if(CurrentTerritory == null) CurrentTerritory = new();

        LocalPlayerContentId = cId;
        LocalPlayerExists = true;
        LocalPlayerPosition = localPlayer.Position;
        LocalPlayerRotation = c->DrawObject->Rotation;

        CurrentTerritory.Ward = CurrentWard;
        CurrentTerritory.Plot = CurrentPlot;
        CurrentTerritory.Room = CurrentRoom;
        CurrentTerritory.Floor = CurrentFloor;

        if(CurrentTerritory.WorldId != c->CurrentWorld) {
            CurrentTerritory.WorldId = c->CurrentWorld;
            CurrentTerritory.WorldName = GetWorldName(CurrentTerritory.WorldId);
        }
        if(CurrentTerritory.TerritoryId != Services.ClientState.TerritoryType && CurrentTerritory.RawTerritoryId != Services.ClientState.TerritoryType) {
            CurrentTerritory.RawTerritoryId = Services.ClientState.TerritoryType;
            CurrentTerritory.TerritoryId = GetCurrentTerritoryId();
            CurrentTerritory.TerritoryName = GetTerritoryName(CurrentTerritory.TerritoryId);
            CurrentTerritory.TerritorySubName = GetTerritorySubName(CurrentTerritory.TerritoryId, CurrentTerritory.Plot > 0);
        }

        if(!CurrentTerritory.Matches(PreviousTerritory, false)) {
            IsLoadingTerritory = !c->GetIsTargetable();
            PreviousTerritory = new TerritoryData(CurrentTerritory);
            TerritoryChanged?.Invoke(false, IsLoadingTerritory, CurrentTerritory);
        }

        if(IsLoadingTerritory && c->GetIsTargetable()) {
            IsLoadingTerritory = false;
            TerritoryLoaded?.Invoke(CurrentTerritory);
        }

        if(IsInitialLoad) {
            IsInitialLoad = false;
            InitialLoad?.Invoke(CurrentTerritory);
        }
    }

    public Region GetRegionFromWorld(uint worldId) {
        return Worlds.FirstOrDefault(w => w.Value.Any(x => x.Id == worldId)).Key;
    }
    public string GetWorldName(uint worldId) {
        var world = Services.DataManager.GetExcelSheet<World>().GetRowOrDefault(worldId);
        return world.HasValue ? world.Value.Name.ToString() : "";
    }
    private unsafe uint GetCurrentTerritoryId() {
        var manager = HousingManager.Instance();
        if(manager is not null && manager->IsInside()) {
            return HousingManager.GetOriginalHouseTerritoryTypeId();
        } else {
            return Services.ClientState.TerritoryType;
        }
    }
    public string GetTerritoryName(uint territoryId) {
        var rt = ResidentialTerritories.FirstOrDefault(x => x.Id == territoryId);
        if(rt != null) return rt.Name;
        var nrt = NonResidentialTerritories.FirstOrDefault(x => x.Id == territoryId);
        return nrt?.Name ?? $"Unknown ({territoryId})";
    }
    public string GetTerritorySubName(uint territoryId, bool isPlot) {
        var rt = ResidentialTerritories.FirstOrDefault(x => x.Id == territoryId);
        if(rt == null) return string.Empty;
        var subNull = string.IsNullOrEmpty(rt.SubName);
        var subName = subNull && isPlot ? "Garden" : subNull ? string.Empty : rt.SubName;
        return subName;
    }

    private void EnsureDespawn() {
        LocalPlayerExists = false;
        if(PreviousTerritory == null) return;
        CurrentTerritory = null;
        PreviousTerritory = null;
        TerritoryChanged?.Invoke(true, false, null);
    }

    public TerritoryData GetTerritoryData(TerritoryPixProperties t, bool persistent) {
        return new TerritoryData() {
            WorldId = t.WorldId,
            TerritoryId = t.TerritoryId,
            Ward = t.Ward,
            Plot = t.Plot,
            Room = persistent ? (short)0 : t.Room,
            Floor = persistent ? Floor.None : t.Floor,

            WorldName = GetWorldName(t.WorldId),
            TerritoryName = GetTerritoryName(t.TerritoryId),
            TerritorySubName = GetTerritorySubName(t.TerritoryId, t.Plot > 0)
        };
    }

    public string GetResidenceFormatted(TerritoryData t) {
        string res = string.Empty;
        if(t.Ward > 0) res += $"W{t.Ward}";
        if(t.Plot > 0) res += $" P{t.Plot}";
        if(t.Room > 0) res += $" R{t.Room}";
        if(t.Floor != Floor.None) res += $" F{(uint)(t.Floor - 1)}";
        return res;
    }
    
    public override Task Dispose() {
        EnsureDespawn();
        return Task.CompletedTask;
    }
}
