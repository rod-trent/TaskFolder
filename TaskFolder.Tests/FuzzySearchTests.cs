using TaskFolder.Services;
using Xunit;

namespace TaskFolder.Tests
{
    public class FuzzySearchTests
    {
        // ── Score tiers ──────────────────────────────────────────────────────────

        [Fact]
        public void Score_ExactMatch_Returns2000()
        {
            Assert.Equal(2000, FuzzySearch.Score("vivaldi", "vivaldi"));
        }

        [Fact]
        public void Score_SubstringMatch_ReturnsAbove1000()
        {
            int score = FuzzySearch.Score("Vivaldi Browser", "Vivaldi");
            Assert.True(score >= 1000, $"Expected >= 1000, got {score}");
        }

        [Fact]
        public void Score_FuzzyCharOrder_ReturnsPositive()
        {
            int score = FuzzySearch.Score("Visual Studio Code", "vsc");
            Assert.True(score > 0, $"Expected > 0 for fuzzy match, got {score}");
        }

        [Fact]
        public void Score_NoMatch_ReturnsZero()
        {
            Assert.Equal(0, FuzzySearch.Score("Notepad", "xyz"));
        }

        [Fact]
        public void Score_EmptyQuery_ReturnsOne()
        {
            Assert.Equal(1, FuzzySearch.Score("anything", ""));
        }

        [Fact]
        public void Score_EmptyTarget_ReturnsZero()
        {
            Assert.Equal(0, FuzzySearch.Score("", "abc"));
        }

        // ── Case insensitivity ───────────────────────────────────────────────────

        [Fact]
        public void Score_CaseInsensitive_ExactMatch()
        {
            Assert.Equal(2000, FuzzySearch.Score("VIVALDI", "vivaldi"));
        }

        [Fact]
        public void Score_CaseInsensitive_FuzzyMatch()
        {
            int score = FuzzySearch.Score("Microsoft Edge", "ME");
            Assert.True(score > 0);
        }

        // ── Ordering ─────────────────────────────────────────────────────────────

        [Fact]
        public void Score_SubstringBeatsCharOrder()
        {
            int sub   = FuzzySearch.Score("Chrome Browser", "chrome");
            int fuzzy = FuzzySearch.Score("Calculator Remote", "chrome");
            Assert.True(sub > fuzzy, $"substring={sub} should beat fuzzy={fuzzy}");
        }

        // ── Matches helper ───────────────────────────────────────────────────────

        [Fact]
        public void Matches_ReturnsTrue_WhenScorePositive()
        {
            Assert.True(FuzzySearch.Matches("Notepad++", "note"));
        }

        [Fact]
        public void Matches_ReturnsFalse_WhenNoMatch()
        {
            Assert.False(FuzzySearch.Matches("Paint", "xyz"));
        }
    }
}
