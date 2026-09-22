using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class SweepPresentationOptimizationTests
{
    [Test]
    public void CompletePresentationOptimization_IsAppliedToAssetsAndScene()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = SweepPresentationOptimizer.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);
    }

    [Test]
    public void GrowthScaling_KeepsPlayerBodyAndBlackHoleAbilitiesPredictable()
    {
        Assert.That(PlayerController.CalculateGrowthMultiplier(2.7f, 1f), Is.EqualTo(2.7f).Within(0.0001f));
        Assert.That(PlayerController.CalculateGrowthMultiplier(2.7f, 0.5f), Is.EqualTo(1.85f).Within(0.0001f));

        float maximumSuckRadius = PetAI.CalculateScaledAbility(
            SweepPresentationOptimizer.BlackHoleSuckRadius,
            2.7f,
            SweepPresentationOptimizer.BlackHoleRadiusGrowthWeight);
        float maximumSuckForce = PetAI.CalculateScaledAbility(
            SweepPresentationOptimizer.BlackHoleSuckForce,
            2.7f,
            SweepPresentationOptimizer.BlackHoleForceGrowthWeight);

        Assert.That(maximumSuckRadius, Is.EqualTo(7.8052f).Within(0.001f));
        Assert.That(maximumSuckForce, Is.EqualTo(21.84f).Within(0.001f));
        Assert.That(maximumSuckRadius, Is.LessThan(
            SweepPresentationOptimizer.BlackHoleSuckRadius * 2.7f),
            "Range should grow with the character without exploding linearly at maximum level.");
    }

    [Test]
    public void RuntimeMix_HasHeadroomAndClampsExternalVolume()
    {
        Assert.That(AudioManager.CalculateBusGain(0.92f, 0.82f, 1f), Is.EqualTo(0.7544f).Within(0.0001f));
        Assert.That(AudioManager.CalculateBusGain(2f, 2f, 2f), Is.EqualTo(1f));
        Assert.That(AudioManager.CalculateBusGain(-1f, 0.8f, 1f), Is.EqualTo(0f));
    }

    [Test]
    public void BurstPool_CoversDenseClimaxWithoutPerHitCreation()
    {
        Assert.That(DemoWorldBurst.PoolCapacity, Is.GreaterThanOrEqualTo(12));
    }

    [Test]
    public void FeedbackOverlay_FindsCanvasElsewhereInTheSameScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject owner = new GameObject("UI manager root");
        GameObject canvasObject = new GameObject("Sibling canvas", typeof(Canvas), typeof(CanvasScaler));
        try
        {
            DemoFeedbackOverlay overlay = DemoFeedbackOverlay.Attach(owner.transform, null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.gameObject, Is.EqualTo(canvasObject));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(canvasObject);
        }
    }
}
