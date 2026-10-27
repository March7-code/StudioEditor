using NUnit.Framework;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class ImportedLightmapTests
    {
        [Test]
        public void MovedMapRebindsAfterSourceUnloadAndKeepsHostSlots()
        {
            var previous = LightmapSettings.lightmaps;var mode = LightmapSettings.lightmapsMode;
            var root = new GameObject("Imported baked map");var host = new Texture2D(1,1);
            var imported = new Texture2D(1,1);var direction = new Texture2D(1,1);
            try
            {
                LightmapSettings.lightmaps = new[]{new LightmapData{lightmapColor=host},
                    new LightmapData{lightmapColor=imported,lightmapDir=direction}};
                var renderer = root.AddComponent<MeshRenderer>();renderer.lightmapIndex=1;
                var st = new Vector4(.4f,.6f,.2f,.1f);renderer.lightmapScaleOffset=st;
                var binding = ImportedLightmapBinding.Capture(root);
                Assert.That(binding.RendererCount,Is.EqualTo(1));
                // Conversion clears indices and unloading removes source slots.
                renderer.lightmapIndex=-1;
                LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=host}};
                binding.Bind();
                Assert.That(LightmapSettings.lightmaps[0].lightmapColor,Is.SameAs(host));
                Assert.That(LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapColor,Is.SameAs(imported));
                Assert.That(LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapDir,Is.SameAs(direction));
                Assert.That(renderer.lightmapScaleOffset,Is.EqualTo(st));
                Assert.That(LightmapSettings.lightmapsMode,Is.EqualTo(LightmapsMode.CombinedDirectional));
                Object.DestroyImmediate(root);
                Assert.That(LightmapSettings.lightmaps[0].lightmapColor,Is.SameAs(host));
                Assert.That(LightmapSettings.lightmaps[1].lightmapColor,Is.Null);
                Assert.That(LightmapSettings.lightmapsMode,Is.EqualTo(mode));
            }
            finally
            {
                if(root!=null)Object.DestroyImmediate(root);
                LightmapSettings.lightmaps=previous;LightmapSettings.lightmapsMode=mode;
                Object.DestroyImmediate(host);Object.DestroyImmediate(imported);Object.DestroyImmediate(direction);
            }
        }

        [Test]
        public void SharedMapsSurviveUntilTheLastBindingIsRemoved()
        {
            var previous=LightmapSettings.lightmaps;var mode=LightmapSettings.lightmapsMode;
            var a=new GameObject("Map A");var b=new GameObject("Map B");var texture=new Texture2D(1,1);
            try
            {
                LightmapSettings.lightmaps=new[]{new LightmapData{lightmapColor=texture}};
                a.AddComponent<MeshRenderer>().lightmapIndex=0;
                b.AddComponent<MeshRenderer>().lightmapIndex=0;
                var first=ImportedLightmapBinding.Capture(a);var second=ImportedLightmapBinding.Capture(b);
                LightmapSettings.lightmaps=System.Array.Empty<LightmapData>();first.Bind();second.Bind();
                Assert.That(LightmapSettings.lightmaps.Length,Is.EqualTo(1));
                Object.DestroyImmediate(a);
                Assert.That(LightmapSettings.lightmaps[b.GetComponent<Renderer>().lightmapIndex].lightmapColor,Is.SameAs(texture));
                Object.DestroyImmediate(b);Assert.That(LightmapSettings.lightmaps[0].lightmapColor,Is.Null);
            }
            finally
            {
                if(a!=null)Object.DestroyImmediate(a);if(b!=null)Object.DestroyImmediate(b);
                LightmapSettings.lightmaps=previous;LightmapSettings.lightmapsMode=mode;Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void LegacyRgbmDecodeProducesHdrAndRestoresRenderState()
        {
            var decoder=new Material(Shader.Find("Hidden/StudioEditor/KoikatsuLightmapDecode"));
            var encoded=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var readback=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                encoded.SetPixel(0,0,new Color(.2f,.4f,.6f,.5f));encoded.Apply();
                GL.sRGBWrite=false;Graphics.Blit(encoded,target,decoder);RenderTexture.active=target;
                readback.ReadPixels(new Rect(0,0,1,1),0,0);readback.Apply();var result=readback.GetPixel(0,0);
                Debug.Log($"RGBM_DECODE_GPU encoded={encoded.GetPixel(0,0):F6} result={result:F6} target={target.graphicsFormat}");
                for(int i=0;i<3;i++)Assert.That(result[i],Is.EqualTo((.2f+.2f*i)*34.493242f*Mathf.Pow(.5f,2.2f)).Within(.001f));
            }
            finally
            {
                RenderTexture.active=active;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(decoder);Object.DestroyImmediate(encoded);Object.DestroyImmediate(readback);
            }
        }
    }
}
