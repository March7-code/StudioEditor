using System.IO;
using System.Linq;
using NUnit.Framework;
using StudioEditor.ReferenceModels;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuMaterialEditorRendererTests
    {
        // This card hides MoreAccessories hair slots (for example slot 21) with
        // MaterialEditor's RendererPropertyList (Enabled = 0), not showAccessory.
        [Test]
        public void RendererEnabledOverridesAreScopedByCoordinateSlotAndName()
        {
            const string path = "D:/koikatsu/Koikatu/UserData/chara/female/下载/小朋友/KK_298172.png";
            if (!File.Exists(path)) Assert.Ignore("Local reference card is unavailable.");
            var data = KoikatsuMaterialEditorData.Read(KoikatsuCardReader.Read(path).Blocks);
            Assert.That(data, Is.Not.Null);
            var hidden = data.GetRendererProperties(2, 0, 21, "cf_hair_f_300_00_0_F (Instance)").ToArray();
            Assert.That(hidden, Has.Length.EqualTo(1));
            Assert.That(hidden[0].Property, Is.Zero);
            Assert.That(hidden[0].Value, Is.EqualTo("0"));
            Assert.That(data.GetRendererProperties(2, 0, 20, "cf_hair_f_300_00_0_F"), Is.Empty, "Slot must match.");
            Assert.That(data.GetRendererProperties(2, 1, 21, "cf_hair_f_300_00_0_F"), Is.Empty, "Accessory coordinate must match.");
            Assert.That(data.GetRendererProperties(2, 0, 21, "cf_hair_f_300_00_0_FL2x"), Is.Empty, "Renderer name must match exactly.");
        }
    }
}
