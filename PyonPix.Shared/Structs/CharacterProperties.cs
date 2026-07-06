using System.Drawing;
using System.Numerics;
using PyonPix.Shared.Extensions;
using PyonPix.Shared.Structs.Pix;
using PyonPix.Shared.Sync.Dto.Client;

namespace PyonPix.Shared.Structs;

public class CharacterProperties : ILocal<SyncedCharacterProperties> {
    public string Alias = string.Empty;

    public Vector3 AliasColourA = Vector3.One;
    public Vector3 AliasColourB = Vector3.One;
    public Vector3 AliasGlowA = Vector3.One;
    public Vector3 AliasGlowB = Vector3.One;
    public AnimationType AliasAnimationType = AnimationType.Static;

    public Vector3 PixColourA = Vector3.One;
    public Vector3 PixColourB = Vector3.One;
    public Vector3 PixGlowA = Vector3.One;
    public Vector3 PixGlowB = Vector3.One;
    public AnimationType PixAnimationType = AnimationType.Static;

    public SyncedCharacterProperties ToSynced() {
        return new SyncedCharacterProperties {
            Alias = Alias,
            AliasStyle = new() {
                ColourA = ColorTranslator.ToHtml(AliasColourA.ToColor()),
                ColourB = ColorTranslator.ToHtml(AliasColourB.ToColor()),
                GlowA = ColorTranslator.ToHtml(AliasGlowA.ToColor()),
                GlowB = ColorTranslator.ToHtml(AliasGlowB.ToColor()),
                AnimationType = AliasAnimationType,
            },
            PixStyle = new() {
                ColourA = ColorTranslator.ToHtml(PixColourA.ToColor()),
                ColourB = ColorTranslator.ToHtml(PixColourB.ToColor()),
                GlowA = ColorTranslator.ToHtml(PixGlowA.ToColor()),
                GlowB = ColorTranslator.ToHtml(PixGlowB.ToColor()),
                AnimationType = PixAnimationType,
            }
        };
    }

    public bool Equals(CharacterProperties? other, bool isSubscriber) {
        if(other == null) return false;
        var a = ToSynced();
        var b = other.ToSynced();
        if(a.Alias != other.Alias) return false;
        if(!isSubscriber) return true;
        if(a.AliasStyle?.ColourA != b.AliasStyle?.ColourA) return false;
        if(a.AliasStyle?.ColourB != b.AliasStyle?.ColourB) return false;
        if(a.AliasStyle?.GlowA != b.AliasStyle?.GlowA) return false;
        if(a.AliasStyle?.GlowB != b.AliasStyle?.GlowB) return false;
        if(a.AliasStyle?.AnimationType != b.AliasStyle?.AnimationType) return false;
        if(a.PixStyle?.ColourA != b.PixStyle?.ColourA) return false;
        if(a.PixStyle?.ColourB != b.PixStyle?.ColourB) return false;
        if(a.PixStyle?.GlowA != b.PixStyle?.GlowA) return false;
        if(a.PixStyle?.GlowB != b.PixStyle?.GlowB) return false;
        if(a.PixStyle?.AnimationType != b.PixStyle?.AnimationType) return false;
        return true;
    }
}

public class SyncedCharacterProperties : ISynced<CharacterProperties> {
    public string Alias { get; set; } = string.Empty;
    public StyleDto? AliasStyle { get; set; }
    public StyleDto? PixStyle { get; set; }

    public void ApplyTo(CharacterProperties target) {
        target.Alias = Alias;

        target.AliasColourA = AliasStyle?.ColourA?.ToVector3() ?? Vector3.One;
        target.AliasColourB = AliasStyle?.ColourB?.ToVector3() ?? target.AliasColourA;
        target.AliasGlowA = AliasStyle?.GlowA?.ToVector3() ?? target.AliasColourA;
        target.AliasGlowB = AliasStyle?.GlowB?.ToVector3() ?? target.AliasColourA;
        target.AliasAnimationType = AliasStyle?.AnimationType ?? AnimationType.Static;

        target.PixColourA = PixStyle?.ColourA?.ToVector3() ?? Vector3.One;
        target.PixColourB = PixStyle?.ColourB?.ToVector3() ?? target.PixColourA;
        target.PixGlowA = PixStyle?.GlowA?.ToVector3() ?? target.PixColourA;
        target.PixGlowB = PixStyle?.GlowB?.ToVector3() ?? target.PixColourA;
        target.PixAnimationType = PixStyle?.AnimationType ?? AnimationType.Static;
    }
}
