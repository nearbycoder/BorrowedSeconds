using BorrowedSeconds.Game;
using NUnit.Framework;
using UnityEngine;

namespace BorrowedSeconds.Tests
{
    /// <summary>
    /// Settings > Graphics fidelity (<see cref="GraphicsFidelity"/>): at least four steps, Low to Ultra;
    /// High is the game's original look and the default (old saves included); each step costs at least
    /// as much as the one below it.
    /// </summary>
    public class FidelityTests
    {
        static GraphicsFidelity.Step[] S => GraphicsFidelity.Steps;

        [Test]
        public void FourStepsLowToUltra()
        {
            Assert.That(S.Length, Is.GreaterThanOrEqualTo(4));
            Assert.AreEqual("Low", S[0].Name);
            Assert.AreEqual("Ultra", S[S.Length - 1].Name);
            Assert.AreEqual("High", S[GraphicsFidelity.Default].Name);
        }

        [Test]
        public void HighIsTheOriginalLook()
        {
            // the values the game shipped with before the setting (ProjectSetup.ConfigureUrp, WorldEnvironment.Build, WatchStage)
            var h = S[GraphicsFidelity.Default];
            Assert.IsTrue(h.SoftShadows);
            Assert.AreEqual(4096, h.ShadowResolution);
            Assert.AreEqual(2, h.Cascades);
            Assert.AreEqual(4, h.Msaa);
            Assert.AreEqual(GraphicsFidelity.Aa.SmaaHigh, h.Antialiasing);
            Assert.IsTrue(h.BloomHalfRes);
            Assert.IsTrue(h.BloomHighQuality);
            Assert.AreEqual(0.4f, h.AoIntensity); // PC_Renderer's SSAO: intensity 0.4, radius 0.3, full resolution, bilateral blur
            Assert.AreEqual(0.3f, h.AoRadius);
            Assert.IsFalse(h.AoDownsample);
            Assert.IsFalse(h.AoFastBlur);
            Assert.IsFalse(h.GlowLights);
            Assert.AreEqual(1f, h.Particles);
            Assert.AreEqual(1f, h.WatchTexture);
            Assert.AreEqual(8, h.WatchMsaa);
        }

        [Test]
        public void EachStepCostsAtLeastTheOneBelow()
        {
            for (int i = 1; i < S.Length; i++)
            {
                var a = S[i - 1];
                var b = S[i];
                Assert.GreaterOrEqual(b.ShadowResolution, a.ShadowResolution, b.Name);
                Assert.GreaterOrEqual(b.Cascades, a.Cascades, b.Name);
                Assert.GreaterOrEqual(b.Msaa, a.Msaa, b.Name);
                Assert.GreaterOrEqual((int)b.Antialiasing, (int)a.Antialiasing, b.Name);
                Assert.GreaterOrEqual(b.Particles, a.Particles, b.Name);
                Assert.GreaterOrEqual(b.WatchTexture, a.WatchTexture, b.Name);
                Assert.GreaterOrEqual(b.WatchMsaa, a.WatchMsaa, b.Name);
                Assert.IsTrue(!a.SoftShadows || b.SoftShadows, b.Name);
                Assert.IsTrue(!a.BloomHighQuality || b.BloomHighQuality, b.Name);
                Assert.GreaterOrEqual(b.AoIntensity, a.AoIntensity, b.Name);
                Assert.GreaterOrEqual(b.AoRadius, a.AoRadius, b.Name);
                Assert.IsTrue(!b.AoDownsample || a.AoDownsample || !a.AmbientOcclusion, b.Name);
                Assert.IsTrue(!a.GlowLights || b.GlowLights, b.Name);
            }
            var low = S[0];
            var ultra = S[S.Length - 1];
            Assert.IsFalse(low.SoftShadows || low.AmbientOcclusion || low.GlowLights);
            Assert.IsTrue(ultra.AmbientOcclusion && ultra.GlowLights);
            Assert.Greater(ultra.AoIntensity, S[GraphicsFidelity.Default].AoIntensity);
            Assert.Greater(ultra.Particles, 1f);
            Assert.Less(low.Particles, 1f);
        }

        [Test]
        public void EveryStepKeepsTheBloomVariantTheBuildCarries()
        {
            // builds only carry high-quality bloom (the only setting the game had); without it beams lost their glow
            foreach (var s in S) Assert.IsTrue(s.BloomHighQuality, s.Name);
        }

        [Test]
        public void EveryStepSaysWhatItChanges()
        {
            foreach (var s in S)
            {
                Assert.IsFalse(string.IsNullOrEmpty(s.Summary), s.Name);
                Assert.LessOrEqual(s.Summary.Length, 110, s.Name + ": the line must fit under the Settings list");
            }
        }

        [Test]
        public void ClampKeepsTheStepInRange()
        {
            Assert.AreEqual(0, GraphicsFidelity.Clamp(-3));
            Assert.AreEqual(S.Length - 1, GraphicsFidelity.Clamp(99));
            Assert.AreEqual(1, GraphicsFidelity.Clamp(1));
        }

        [Test]
        public void ANewSaveAndAnOldSaveAreHigh()
        {
            Assert.AreEqual(GraphicsFidelity.Default, new SaveData().fidelity);
            // a save written before the setting: no "fidelity" key at all
            var old = JsonUtility.FromJson<SaveData>("{\"master\":0.5,\"music\":0.4,\"renderScale\":0.7}");
            Assert.AreEqual(GraphicsFidelity.Default, old.fidelity);
            Assert.AreEqual(0.5f, old.master);
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(new SaveData { fidelity = 3 }));
            Assert.AreEqual(3, back.fidelity);
        }
    }
}
