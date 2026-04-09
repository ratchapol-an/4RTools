using System.Threading;
using _4RTools.Utils;
using Xunit;

namespace _4RTools.Tests
{
    public class InputCoordinatorTests
    {
        public InputCoordinatorTests()
        {
            InputCoordinator.ResetForTests();
        }

        [Fact]
        public void CanStartNovaSequence_IsTrue_WhenNoRecentHighPriorityAction()
        {
            bool result = InputCoordinator.CanStartNovaSequence();
            Assert.True(result);
        }

        [Fact]
        public void BeginNovaSequence_IsAtomic_AndCannotBeginTwiceWithoutEnding()
        {
            bool first = InputCoordinator.BeginNovaSequence();
            bool second = InputCoordinator.BeginNovaSequence();

            Assert.True(first);
            Assert.False(second);

            InputCoordinator.EndNovaSequence();

            bool third = InputCoordinator.BeginNovaSequence();
            Assert.True(third);
            InputCoordinator.EndNovaSequence();
        }

        [Fact]
        public void CanStartNovaSequence_RespectsQuietWindowAfterHighPriorityAction()
        {
            InputCoordinator.MarkHighPriorityActionForTests();

            bool immediate = InputCoordinator.CanStartNovaSequence(quietWindowMs: 150);
            Assert.False(immediate);

            Thread.Sleep(180);
            bool afterWindow = InputCoordinator.CanStartNovaSequence(quietWindowMs: 150);
            Assert.True(afterWindow);
        }

        [Fact]
        public void CanStartNovaSequence_IsFalse_WhileNovaSequenceActive()
        {
            bool started = InputCoordinator.BeginNovaSequence();
            Assert.True(started);

            bool canStart = InputCoordinator.CanStartNovaSequence();
            Assert.False(canStart);

            InputCoordinator.EndNovaSequence();
            Assert.True(InputCoordinator.CanStartNovaSequence());
        }
    }
}
