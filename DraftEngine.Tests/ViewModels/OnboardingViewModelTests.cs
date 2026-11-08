using DraftEngine.App.ViewModels;

namespace DraftEngine.Tests.ViewModels;

/// <summary>Tests for the three-screen first-run onboarding dialog.</summary>
public class OnboardingViewModelTests
{
    [Fact]
    public void StartsOnTheFirstOfThreeScreens()
    {
        var viewModel = new OnboardingViewModel();

        Assert.Equal(3, viewModel.StepCount);
        Assert.Equal(0, viewModel.StepIndex);
        Assert.True(viewModel.IsFirst);
        Assert.False(viewModel.IsLast);
        Assert.Equal("Step 1 of 3", viewModel.StepLabel);
        Assert.Equal("Next", viewModel.PrimaryButtonText);
    }

    [Fact]
    public void Next_WalksThroughTheScreensAndCompletes()
    {
        var viewModel = new OnboardingViewModel();
        var completed = 0;
        viewModel.Completed += (_, _) => completed++;

        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        Assert.Equal(2, viewModel.StepIndex);
        Assert.True(viewModel.IsLast);
        Assert.Equal("Get started", viewModel.PrimaryButtonText);
        Assert.Equal(0, completed);

        viewModel.NextCommand.Execute(null);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Back_DoesNotGoBelowTheFirstScreen()
    {
        var viewModel = new OnboardingViewModel();

        viewModel.BackCommand.Execute(null);

        Assert.Equal(0, viewModel.StepIndex);
    }

    [Fact]
    public void Skip_CompletesImmediately()
    {
        var viewModel = new OnboardingViewModel();
        var completed = 0;
        viewModel.Completed += (_, _) => completed++;

        viewModel.SkipCommand.Execute(null);

        Assert.Equal(1, completed);
    }
}
