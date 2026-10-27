using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuPhysicsImportTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void ColliderTracksTheAnimatedBodyTransform()
        {
            var owner = new GameObject("Moving chest collider");
            try
            {
                var collider = new KoikatsuDynamicBoneCollider(owner.transform,
                    new KoikatsuDynamicBoneColliderDefinition("", Vector3.zero, .1f, 0, 0, 0));
                owner.transform.position = new Vector3(1, 2, 3);
                owner.transform.localScale = Vector3.one * 2;
                var point = owner.transform.position + Vector3.forward * .02f;
                collider.Collide(ref point, .03f);
                Assert.That(Vector3.Distance(owner.transform.position, point), Is.EqualTo(.23f).Within(.00001f));
                owner.transform.position += Vector3.right;
                var previous = point;
                collider.Collide(ref point, .03f);
                Assert.That(point, Is.EqualTo(previous));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void PhysicsRootRemapsEvenAfterDuplicateSkeletonWasRemoved()
        {
            var body = new GameObject("Body");
            var part = new GameObject("Part");
            try
            {
                var target = Child(body.transform, "root/with:punctuation");
                var duplicate = Child(part.transform, target.name);
                var path = KoikatsuClothesRendererMapLoader.CreateRuntimePath(part.transform, duplicate);
                Object.DestroyImmediate(duplicate.gameObject);
                Assert.That(KoikatsuDynamicBoneMetadata.TryResolveTransform(path,
                    KoikatsuSpringBoneMetadataLoader.BuildRuntimeTransformMap(part.transform),
                    new Dictionary<string, Transform> { [target.name] = target }, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(target));
            }
            finally { Object.DestroyImmediate(body); Object.DestroyImmediate(part); }
        }

        [Test]
        public void EndLengthScalesTheAuthoredLastBoneRatherThanUsingMetres()
        {
            var owner = new GameObject("Ribbon");
            try
            {
                var root = Child(owner.transform, "Root");
                var tip = Child(root, "Tip"); tip.localPosition = Vector3.down * .08f;
                var spring = owner.AddComponent<KoikatsuSpringBone>(); spring.enabled = false;
                spring.Configure(root, 60, .1f, .1f, .3f, .8f, .018f,
                    1, Vector3.zero, Vector3.zero, Vector3.zero, 0, null, null, true);
                var values = Particles(spring);
                Assert.That(values.Length, Is.EqualTo(3));
                var offset = (Vector3)values[2].GetType().GetField("VirtualOffset").GetValue(values[2]);
                Assert.That(offset.magnitude, Is.EqualTo(.08f).Within(.000001f));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void CollisionRadiusUsesCurveAtAccumulatedBoneLength()
        {
            var owner = new GameObject("Ribbon curve");
            try
            {
                var root = Child(owner.transform, "Root");
                var middle = Child(root, "Middle"); middle.localPosition = Vector3.down * .02f;
                var tip = Child(middle, "Tip"); tip.localPosition = Vector3.down * .08f;
                var spring = owner.AddComponent<KoikatsuSpringBone>(); spring.enabled = false;
                spring.Configure(root, 60, .1f, .1f, .3f, .8f, .02f,
                    0, Vector3.zero, Vector3.zero, Vector3.zero, 0, null, null, true,
                    new[] { null, null, null, null, AnimationCurve.Linear(0, 0, 1, 2) });
                var values = Particles(spring);
                Assert.That((float)values[1].GetType().GetField("Radius").GetValue(values[1]), Is.EqualTo(.008f).Within(.000001f));
                Assert.That((float)values[2].GetType().GetField("Radius").GetValue(values[2]), Is.EqualTo(.04f).Within(.000001f));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void FkOwnsItsBonesAndPhysicsResumesWhenFkReleasesThem()
        {
            var owner = new GameObject("FK skirt");
            try
            {
                var root = Child(owner.transform, "Root");
                Child(root, "Tip").localPosition = Vector3.down * .1f;
                var spring = owner.AddComponent<KoikatsuSpringBone>(); spring.enabled = false;
                spring.Configure(root, 60, .1f, .1f, .3f, .8f, .01f,
                    0, Vector3.zero, Vector3.zero, Vector3.down * .001f, 0, null, null, true);
                spring.SetSimulationEnabled(true);
                spring.SetFkAllowed(false);
                root.localRotation = Quaternion.Euler(35, 20, 8);
                Assert.That(spring.enabled, Is.False);
                Assert.That(spring.Allowed, Is.False);
                spring.SetSimulationEnabled(true);
                Assert.That(spring.enabled, Is.False);
                Assert.That(Quaternion.Angle(root.localRotation, Quaternion.Euler(35, 20, 8)), Is.LessThan(.05f));
                spring.SetFkAllowed(true);
                Assert.That(spring.enabled, Is.True);
                spring.SetSimulationEnabled(false);
                spring.SetFkAllowed(false); spring.SetFkAllowed(true);
                Assert.That(spring.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase("p_o_top_sailor01_02", "cpo_sailor_b_00.unity3d", "cf_d_backsk_00")]
        [TestCase("p_o_top_sailor01_03", "cpo_sailor_c_00.unity3d", "cf_j_spinesk_00")]
        public void SourceCollarUsesSharedSkeletonAndAllBodyColliders(string asset, string file, string rootName)
        {
            const string folder = "D:/koikatsu/Koikatu/abdata/chara/";
            if (!File.Exists(folder + file)) Assert.Ignore("Local source clothing is not installed.");
            using var baseLease = KoikatsuAssetBundleCache.Acquire(folder + "oo_base.unity3d");
            using var partLease = KoikatsuAssetBundleCache.Acquire(folder + file);
            var body = Object.Instantiate(baseLease.Bundle.LoadAsset<GameObject>("p_cf_body_bone"));
            var part = Object.Instantiate(partLease.Bundle.LoadAsset<GameObject>(asset));
            try
            {
                body.SetActive(false); part.SetActive(false);
                var bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                KoikatsuDynamicBoneMetadata.AttachCharacterColliders(new KoikatsuBundleSource(folder + "oo_base.unity3d"), "p_cf_body_bone", body);
                var colliders = body.GetComponent<KoikatsuCharacterColliders>().Colliders;
                Assert.That(colliders.Count, Is.EqualTo(24));
                Assert.That(KoikatsuSpringBoneMetadataLoader.Attach(new KoikatsuBundleSource(folder + file), asset, part, true, bones, colliders), Is.EqualTo(1));
                var spring = part.GetComponent<KoikatsuSpringBone>();
                Assert.That(typeof(KoikatsuSpringBone).GetField("springRoot", Fields).GetValue(spring), Is.SameAs(bones[rootName]));
                foreach (var p in Particles(spring))
                {
                    var bone = (Transform)p.GetType().GetProperty("Transform").GetValue(p);
                    if (bone != null) Assert.That(bone.IsChildOf(body.transform), Is.True);
                }
                Assert.That(typeof(KoikatsuSpringBone).GetField("colliders", Fields).GetValue(spring), Is.SameAs(colliders));
            }
            finally { Object.DestroyImmediate(part); Object.DestroyImmediate(body); }
        }

        static object[] Particles(KoikatsuSpringBone spring) => ((IEnumerable)typeof(KoikatsuSpringBone).GetField("particles", Fields).GetValue(spring)).Cast<object>().ToArray();
        static Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
    }
}
