using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using SuperliminalHeadTracking.CameraRig;
using Xunit;

namespace SuperliminalHeadTracking.Tests
{
    public class LeanCollisionTests
    {
        [Theory]
        [InlineData(1f, 0.15f, 0.01f, 0.35f)]
        [InlineData(0.5f, 0.15f, 0.01f, 0.2f)]
        [InlineData(0.1f, 0.1f, 0.01f, 0.1f)]
        [InlineData(1f, 0.1f, 0.2f, 0.2f)]
        [InlineData(1f, 0.6f, 0.01f, 0f)]
        public void ClearanceIncludesSurfaceAngleAndNearPlane(
            float approach, float margin, float nearClip, float expected)
        {
            var query = new LeanCollisionQuery((eye, direction, distance) =>
                LineHit.At(0.5f, new Vec3((float)System.Math.Sqrt(1f - approach * approach), 0f, -approach)))
            {
                Margin = margin,
                NearClipPlane = nearClip
            };
            var clamp = new LeanClamp { Settings = new LeanClampSettings { Skin = 0f } };

            Vec3 allowed = clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query);

            Assert.Equal(expected, allowed.Z, 5);
            Assert.True(clamp.InContact);
            Assert.False(clamp.LastQueryFailed);
        }

        [Fact]
        public void QueryReachesBeyondDesiredLeanToMaintainStandoff()
        {
            float traceDistance = 0f;
            var query = new LeanCollisionQuery((eye, direction, distance) =>
            {
                Assert.Equal(new Vec3(1f, 2f, 3f), eye);
                Assert.Equal(Vec3.Forward, direction);
                traceDistance = distance;
                return LineHit.At(0.4f, new Vec3(0f, 0f, -1f));
            }) { Margin = 0.15f };
            var clamp = new LeanClamp { Settings = new LeanClampSettings { Skin = 0f } };

            Vec3 allowed = clamp.Apply(new Vec3(1f, 2f, 3f), Vec3.Forward * 0.3f,
                0.016f, query.Query);

            Assert.Equal(0.9f, traceDistance, 5);
            Assert.Equal(0.25f, allowed.Z, 5);
        }

        [Fact]
        public void FailedQueryIsDistinctFromClearPath()
        {
            LineHit hit = new LineHit();
            var query = new LeanCollisionQuery((eye, direction, distance) => hit);
            var clamp = new LeanClamp { Settings = new LeanClampSettings { Skin = 0f } };

            Assert.Equal(Vec3.Forward, clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query));
            Assert.True(clamp.LastQueryFailed);

            hit = LineHit.Miss;
            Assert.Equal(Vec3.Forward, clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query));
            Assert.False(clamp.LastQueryFailed);
            Assert.False(clamp.InContact);
        }

        [Fact]
        public void CoreTightensImmediatelyAndSmoothsReleaseInDistance()
        {
            LineHit hit = LineHit.At(0.5f, new Vec3(0f, 0f, -1f));
            var query = new LeanCollisionQuery((eye, direction, distance) => hit) { Margin = 0.1f };
            var clamp = new LeanClamp
            {
                Settings = new LeanClampSettings { Skin = 0f, ReleaseSmoothing = 0.9f }
            };
            Assert.Equal(0.4f, clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query).Z, 5);

            hit = LineHit.Miss;
            float released = clamp.Apply(Vec3.Zero, Vec3.Forward * 2f, 0.016f, query.Query).Z;
            Assert.InRange(released, 0.4f, 0.8f);
            Assert.True(clamp.InContact);

            hit = LineHit.At(0.2f, new Vec3(0f, 0f, -1f));
            Assert.Equal(0.1f, clamp.Apply(Vec3.Zero, Vec3.Forward * 2f, 0.016f, query.Query).Z, 5);

            clamp.Reset();
            hit = LineHit.Miss;
            Assert.Equal(Vec3.Forward * 2f,
                clamp.Apply(Vec3.Zero, Vec3.Forward * 2f, 0.016f, query.Query));
            Assert.False(clamp.InContact);
        }

        [Fact]
        public void ZeroLeanClearsPreviousRestrictionWithoutCasting()
        {
            int casts = 0;
            LineHit hit = LineHit.At(0.1f, new Vec3(0f, 0f, -1f));
            var query = new LeanCollisionQuery((eye, direction, distance) =>
            {
                casts++;
                return hit;
            }) { Margin = 0.15f };
            var clamp = new LeanClamp
            {
                Settings = new LeanClampSettings { Skin = 0f, ReleaseSmoothing = 0.9f }
            };
            Assert.Equal(Vec3.Zero, clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query));
            Assert.Equal(Vec3.Zero, clamp.Apply(Vec3.Zero, Vec3.Zero, 0.016f, query.Query));
            Assert.Equal(1, casts);

            hit = LineHit.Miss;
            Assert.Equal(Vec3.Forward, clamp.Apply(Vec3.Zero, Vec3.Forward, 0.016f, query.Query));
            Assert.False(clamp.InContact);
        }
    }
}
