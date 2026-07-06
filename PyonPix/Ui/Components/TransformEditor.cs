using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using PyonPix.Extensions;
using PyonPix.Services.Game;
using PyonPix.Structs.Ui;
using PyonPix.Utility;

namespace PyonPix.Ui.Components;

public class TransformEditor {
    private ImGuizmoOperation GizmoOperation;
    private bool WasUsingTable;
    private bool WasUsingGizmo;

    private bool IsGizmoVisible = false;

    private float IconSize => 20f * ImGuiHelpers.GlobalScale;
    private float Spacing => 1f * ImGuiHelpers.GlobalScale;

    public UIState DrawTable(string id, ref Vector3 pos, ref Quaternion rot, Action<string>? posAction = null, Action<string>? rotAction = null) {
        var scl = Vector3.Zero;
        return DrawTable(id, ref pos, ref rot, ref scl, posAction, rotAction);
    }

    public UIState DrawTable(string id, ref Vector3 pos, ref Quaternion rot, ref Vector3 scl, Action<string>? posAction = null, Action<string>? rotAction = null, Action<string>? sclAction = null) {
        var res = UIState.None;
        var regionWidth = ImGui.GetContentRegionAvail().X;
        float axisWidth = (regionWidth - IconSize) / 3f - (Spacing * 2);

        var right = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rot));
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rot));
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, rot));
        var localPos = new Vector3(Vector3.Dot(pos, right), Vector3.Dot(pos, up), Vector3.Dot(pos, forward));
        var localEuler = rot.QuaternionToEulerDeg();

        bool setToPlayerPos = false;
        bool valueChanged = false;
        bool isAnyActive = false;

        ImGui.PushID(id);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Spacing));

        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.ArrowsAlt, $"{id}posGizmo", IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Translate, tooltip: "Toggle Position Gizmo", size: IconSize)) {
            if(IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Translate) {
                IsGizmoVisible = false;
            } else {
                IsGizmoVisible = true;
                GizmoOperation = ImGuizmoOperation.Translate;
            }
        }

        ImGui.SameLine();
        
        valueChanged |= ImGuiEx.AxisXDrag($"{id}posX", ref localPos.X, axisWidth);
        isAnyActive |= ImGui.IsItemActive();
        posAction?.Invoke($"{id}posX");
        ImGui.SameLine();
        valueChanged |= ImGuiEx.AxisYDrag($"{id}posY", ref localPos.Y, axisWidth);
        isAnyActive |= ImGui.IsItemActive();
        posAction?.Invoke($"{id}posY");
        ImGui.SameLine();
        valueChanged |= ImGuiEx.AxisZDrag($"{id}posZ", ref localPos.Z, axisWidth);
        isAnyActive |= ImGui.IsItemActive();
        posAction?.Invoke($"{id}posZ");

        if(ImGuiEx.IconToggleButton(FontAwesomeIcon.ArrowsSpin, $"{id}rotGizmo", IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Rotate, tooltip: "Toggle Rotation Gizmo", size: IconSize)) {
            if(IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Rotate) {
                IsGizmoVisible = false;
            } else {
                IsGizmoVisible = true;
                GizmoOperation = ImGuizmoOperation.Rotate;
            }
        }

        ImGui.SameLine();
        valueChanged |= ImGuiEx.AxisXDrag($"{id}rotX", ref localEuler.X, axisWidth, 0.01f);
        isAnyActive |= ImGui.IsItemActive();
        rotAction?.Invoke($"{id}rotX");
        ImGui.SameLine();
        valueChanged |= ImGuiEx.AxisYDrag($"{id}rotY", ref localEuler.Y, axisWidth, 0.01f);
        isAnyActive |= ImGui.IsItemActive();
        rotAction?.Invoke($"{id}rotY");
        ImGui.SameLine();
        valueChanged |= ImGuiEx.AxisZDrag($"{id}rotZ", ref localEuler.Z, axisWidth, 0.01f);
        isAnyActive |= ImGui.IsItemActive();
        rotAction?.Invoke($"{id}rotZ");

        if(scl != Vector3.Zero) {
            if(ImGuiEx.IconToggleButton(FontAwesomeIcon.Expand, $"{id}sclGizmo", IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Scale, tooltip: "Toggle Scale Gizmo", size: IconSize)) {
                if(IsGizmoVisible && GizmoOperation == ImGuizmoOperation.Scale) {
                    IsGizmoVisible = false;
                } else {
                    IsGizmoVisible = true;
                    GizmoOperation = ImGuizmoOperation.Scale;
                }
            }

            ImGui.SameLine();
            valueChanged |= ImGuiEx.AxisXDrag($"{id}sclX", ref scl.X, axisWidth);
            isAnyActive |= ImGui.IsItemActive();
            sclAction?.Invoke($"{id}sclX");
            ImGui.SameLine();
            valueChanged |= ImGuiEx.AxisYDrag($"{id}sclY", ref scl.Y, axisWidth);
            isAnyActive |= ImGui.IsItemActive();
            sclAction?.Invoke($"{id}sclY");
            ImGui.SameLine();
            valueChanged |= ImGuiEx.AxisZDrag($"{id}sclZ", ref scl.Z, axisWidth);
            isAnyActive |= ImGui.IsItemActive();
            sclAction?.Invoke($"{id}sclZ");
        }

        ImGui.PopStyleVar();

        if(valueChanged || setToPlayerPos) {
            var newRotation = Quaternion.CreateFromYawPitchRoll(localEuler.Y.DegToRad(), localEuler.X.DegToRad(), localEuler.Z.DegToRad());
            Vector3 worldTranslation = (right * localPos.X) + (up * localPos.Y) + (forward * localPos.Z); // setToPlayerPos ? player.LocalPlayerPosition

            pos = worldTranslation;
            rot = newRotation;

            WasUsingTable = true;
            res = UIState.Using;
        }
        if(!isAnyActive && WasUsingTable) {
            WasUsingTable = false;
            res = UIState.Ended;
        }

        ImGui.PopID();

        return res;
    }

    public void HideGizmo() => IsGizmoVisible = false;

    public UIState DrawGizmo(string id, ref Vector3 pos, ref Quaternion rot, ImGuizmoMode mode = ImGuizmoMode.Local) {
        var scl = Vector3.Zero;
        return DrawGizmo(id, ref pos, ref rot, ref scl, mode);
    }

    public UIState DrawGizmo(string id, ref Vector3 pos, ref Quaternion rot, ref Vector3 scl, ImGuizmoMode mode = ImGuizmoMode.Local) {
        var res = UIState.None;
        if(!IsGizmoVisible) return res;

        ImGui.PushID(id);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        if(ImGui.Begin($"{id}PyonPixGizmo", ref IsGizmoVisible, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoInputs)) {
            if(!IsGizmoVisible || ImGui.IsKeyReleased(ImGuiKey.Escape)) {
                IsGizmoVisible = false;
                ImGui.End();
                ImGui.PopStyleColor(2);
                ImGui.PopID();
                return res;
            }

            ImGui.SetWindowPos(Vector2.Zero);
            ImGui.SetWindowSize(UiUtil.GameResolution);

            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetDrawlist(ImGui.GetWindowDrawList());
            ImGuizmo.SetRect(0, 0, UiUtil.GameWidth, UiUtil.GameHeight);

            var view = CameraService.GetViewMatrix();
            var proj = CameraService.GetProjectionMatrixForGizmo();
            var transform = Matrix4x4.CreateScale(scl == Vector3.Zero ? Vector3.One : scl) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
            bool isUsing = ImGuizmo.IsUsing();
            if(ImGuizmo.Manipulate(ref view, ref proj, GizmoOperation, ImGuizmoMode.Local, ref transform)) {
                if(Matrix4x4.Decompose(transform, out var outScl, out var outRot, out var outPos)) {
                    float eps = 0.0001f;
                    var posChanged = Vector3.DistanceSquared(pos, outPos) > eps * eps;
                    var sclChanged = scl != Vector3.Zero && Vector3.DistanceSquared(scl, outScl) > eps * eps;
                    float dot = Math.Clamp(MathF.Abs(Quaternion.Dot(rot, outRot)), -1f, 1f);
                    var rotChanged = 2f * MathF.Acos(dot) > eps; // radians

                    if(posChanged || rotChanged || sclChanged) {
                        pos = outPos;
                        rot = outRot;
                        scl = outScl;

                        WasUsingGizmo = true;
                        res = UIState.Using;
                    }
                }
            }
            if(!isUsing && WasUsingGizmo) {
                WasUsingGizmo = false;
                res = UIState.Ended;
            }

            ImGui.End();
        }
        ImGui.PopStyleColor(2);
        ImGui.PopID();

        return res;
    }
}
