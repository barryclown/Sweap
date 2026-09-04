using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

public sealed class SweepDemoIntegrityTests
{
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string IntroVideoPath = "Assets/barry/Sweep/Sprites/limit/video.mp4";

    [Test]
    public void BuildSettings_ContainRequiredEnabledScenes()
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        CollectionAssert.Contains(enabledScenes, "Assets/barry/Scenes/StartMenu.unity");
        CollectionAssert.Contains(enabledScenes, "Assets/barry/Scenes/Sweep.unity");
    }

    [Test]
    public void PlayerInput_HasAllDemoActions()
    {
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        Assert.That(actions, Is.Not.Null, $"Missing {InputActionsPath}");

        InputActionMap player = actions.FindActionMap("Player", false);
        Assert.That(player, Is.Not.Null);
        Assert.That(player.FindAction("PointerPress", false), Is.Not.Null);
        Assert.That(player.FindAction("RightPointerPress", false), Is.Not.Null);
        Assert.That(player.FindAction("PointerPosition", false), Is.Not.Null);
        Assert.That(player.FindAction("SwitchMode", false), Is.Not.Null);
    }

    [Test]
    public void IntroVideo_IsImportable()
    {
        VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(IntroVideoPath);
        Assert.That(clip, Is.Not.Null, $"Video failed to import: {IntroVideoPath}");
        Assert.That(clip.width, Is.GreaterThan(0));
        Assert.That(clip.height, Is.GreaterThan(0));
    }

    [TestCase(90f, 60f)]
    [TestCase(60f, 60f)]
    [TestCase(45f, 45f)]
    [TestCase(20f, 45f)]
    public void ExhibitionDuration_IsClamped(float configured, float expected)
    {
        Assert.That(UIManager.ClampExhibitionDuration(configured), Is.EqualTo(expected));
    }

    [Test]
    public void PassiveAbsorption_GetsTenPercent_AndPlayerGetsFullScore()
    {
        Assert.That(BlackHoleObstacle.CalculateAbsorptionScore(50, false, 0.1f), Is.EqualTo(5));
        Assert.That(BlackHoleObstacle.CalculateAbsorptionScore(50, true, 0.1f), Is.EqualTo(50));
        Assert.That(BlackHoleObstacle.CalculateAbsorptionScore(-50, true, 0.1f), Is.EqualTo(0));
    }

    [Test]
    public void ComboMultiplier_RewardsChainAndFrenzy()
    {
        Assert.That(UIManager.CalculateComboMultiplier(1, 0.1f, false, 1.5f), Is.EqualTo(1f));
        Assert.That(UIManager.CalculateComboMultiplier(6, 0.1f, false, 1.5f), Is.EqualTo(1.5f));
        Assert.That(UIManager.CalculateComboMultiplier(6, 0.1f, true, 1.5f), Is.EqualTo(2.25f));
    }

    [Test]
    public void PoolItem_WithLegacyZeroScale_RestoresItsRealScale()
    {
        GameObject go = new GameObject("Pool scale test");
        go.transform.localScale = new Vector3(1.75f, 2.25f, 1f);
        go.SetActive(false);

        try
        {
            PoolScaleProbe probe = go.AddComponent<PoolScaleProbe>();
            Assert.That(probe.initialScale, Is.EqualTo(Vector3.zero));
            probe.CaptureTransformForTest();

            go.transform.localScale = Vector3.zero;
            probe.ResetState();

            Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(1.75f, 2.25f, 1f)));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PoolItem_AfterAbsorbSpin_RestoresItsInitialRotation()
    {
        GameObject go = new GameObject("Pool rotation test");
        Quaternion expected = Quaternion.Euler(315f, 0f, 37f);
        go.transform.rotation = expected;
        go.SetActive(false);

        try
        {
            PoolScaleProbe probe = go.AddComponent<PoolScaleProbe>();
            probe.CaptureTransformForTest();
            go.transform.rotation = Quaternion.Euler(10f, 20f, 170f);
            probe.ResetState();

            Assert.That(Quaternion.Angle(go.transform.rotation, expected), Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}

public sealed class PoolScaleProbe : BasePoolItem
{
    public void CaptureTransformForTest()
    {
        base.OnEnable();
    }
}
