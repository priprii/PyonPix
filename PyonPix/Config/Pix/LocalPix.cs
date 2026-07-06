using System;
using System.Numerics;
using PyonPix.Services.Game;

namespace PyonPix.Config.Pix;

public class LocalPix : BasePix {
    public LocalPix() { }

    public LocalPix(string id, StateService state) {
        Id = id;

        if(state.CurrentTerritory != null) {
            Territory.WorldId = state.CurrentTerritory.WorldId;
            Territory.TerritoryId = state.CurrentTerritory.TerritoryId;
            Territory.Ward = state.CurrentTerritory.Ward;
            Territory.Plot = state.CurrentTerritory.Plot;
            Territory.Room = state.CurrentTerritory.Room;
            Territory.Floor = state.CurrentTerritory.Floor;
        }

        Renderer.Position = new(state.LocalPlayerPosition.X, state.LocalPlayerPosition.Y + 1f, state.LocalPlayerPosition.Z);
        Renderer.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) * state.LocalPlayerRotation;
        Renderer.Scale = new(3f, 1.6875f, 0.03f);

        Light.Position = Vector3.Zero;
        Light.Rotation = Quaternion.Identity;
    }
}
