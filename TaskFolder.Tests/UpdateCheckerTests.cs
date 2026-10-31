using TaskFolder.Services;
using Xunit;

namespace TaskFolder.Tests
{
    /// <summary>
    /// Tests the private IsNewer logic indirectly via the public event contract.
    /// Direct version-parsing tests use a reflection helper so the algorithm can
    /// be verified without changing the class's encapsulation.
    /// </summary>
    public class UpdateCheckerTests
    {
        // Access the private IsNewer method via reflection
        private static bool IsNewer(string latest, string current)
        {
            var method = typeof(UpdateChecker).GetMethod(
                "IsNewer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return method!.Invoke(null, new object[] { latest, current }) is true;
        }

        [Fact]
        public void IsNewer_HigherMinor_ReturnsTrue()
        {
            Assert.True(IsNewer("v1.3.0", "v1.2.0"));
        }

        [Fact]
        public void IsNewer_SameVersion_ReturnsFalse()
        {
            Assert.False(IsNewer("v1.2.0", "v1.2.0"));
        }

        [Fact]
        public void IsNewer_OlderVersion_ReturnsFalse()
        {
            Assert.False(IsNewer("v1.1.0", "v1.2.0"));
        }

        [Fact]
        public void IsNewer_HigherPatch_ReturnsTrue()
        {
            Assert.True(IsNewer("v1.2.1", "v1.2.0"));
        }

        [Fact]
        public void IsNewer_HigherMajor_ReturnsTrue()
        {
            Assert.True(IsNewer("v2.0.0", "v1.9.9"));
        }

        [Fact]
        public void IsNewer_PreReleaseSuffix_StillParsesCorrectly()
        {
            // "v1.3.0-beta" should parse as 1.3.0 which is newer than 1.2.0
            Assert.True(IsNewer("v1.3.0-beta", "v1.2.0"));
        }

        [Fact]
        public void IsNewer_MalformedTag_ReturnsFalse()
        {
            // Unparseable → 0.0.0 which is not newer than any real version
            Assert.False(IsNewer("not-a-version", "v1.2.0"));
        }
    }
}
