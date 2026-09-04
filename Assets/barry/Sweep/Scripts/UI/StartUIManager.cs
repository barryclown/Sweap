using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public class StartUIManager : MonoBehaviour
{
    [Header("=== 主選單 UI ===")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private Button startButton;
    [SerializeField] private Button teachButton;
    [SerializeField] private Button exitButton;

    [Header("=== 說明面板 UI ===")]
    [SerializeField] private GameObject teachPanel;
    [SerializeField] private Image teachImageDisplay;
    [SerializeField] private Sprite[] teachSprites;
    private int currentTeachIndex = 0;
    private bool blockInputThisFrame = false; // 用於防止按鈕點擊與 Update 衝突

    [Header("=== 影片播放設定 ===")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private GameObject videoPanel;

    [Header("=== 場景名稱 ===")]
    [SerializeField] private string gameSceneName = "GameScene";

    private bool isPlayingVideo = false;

    private void OnEnable()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartGame);
        if (teachButton != null) teachButton.onClick.AddListener(OnTeachClicked);
        if (exitButton != null) exitButton.onClick.AddListener(OnExitGame);

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached += OnVideoFinished;
        }
    }

    private void OnDisable()
    {
        if (startButton != null) startButton.onClick.RemoveListener(OnStartGame);
        if (teachButton != null) teachButton.onClick.RemoveListener(OnTeachClicked);
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitGame);

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
        }
    }

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (startPanel != null) startPanel.SetActive(true);
        if (videoPanel != null) videoPanel.SetActive(false);
        if (teachPanel != null) teachPanel.SetActive(false);
    }

    private void Update()
    {
        // 處理教學面板點擊換圖
        if (teachPanel != null && teachPanel.activeSelf)
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (blockInputThisFrame) return; // 如果是開啟面板的那一幀，不做反應
                NextTeachImage();
            }
        }

        // 處理影片跳過
        if (isPlayingVideo && Input.GetMouseButtonDown(0))
        {
            if (blockInputThisFrame) return; // 防止點擊「開始遊戲」的瞬間就跳過影片
            SkipVideo();
        }

        // 每一幀結束前重置輸入擋箭牌
        if (blockInputThisFrame)
        {
            blockInputThisFrame = false;
        }
    }

    private void OnStartGame()
    {
        if (startPanel != null) startPanel.SetActive(false);

        if (videoPlayer != null && videoPanel != null)
        {
            videoPanel.SetActive(true);

            // 修正：重置影片時間到第一幀並開始播放
            videoPlayer.Stop();
            videoPlayer.frame = 0;
            videoPlayer.Play();

            isPlayingVideo = true;
            blockInputThisFrame = true; // 標記此幀不偵測點擊，避免按鈕與跳過功能衝突
        }
        else
        {
            LoadGameScene();
        }
    }

    private void OnVideoFinished(VideoPlayer vp) => LoadGameScene();

    private void SkipVideo()
    {
        if (videoPlayer != null) videoPlayer.Stop();
        LoadGameScene();
    }

    private void LoadGameScene()
    {
        isPlayingVideo = false;
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnTeachClicked()
    {
        if (teachPanel != null)
        {
            teachPanel.SetActive(true);
            teachPanel.transform.SetAsLastSibling();

            currentTeachIndex = 0;
            blockInputThisFrame = true; // 標記此幀不偵測點擊，解決需要「點兩下」的問題

            if (teachSprites != null && teachSprites.Length > 0 && teachImageDisplay != null)
            {
                teachImageDisplay.sprite = teachSprites[0];
            }
        }
    }

    private void NextTeachImage()
    {
        currentTeachIndex++;

        if (teachSprites != null && currentTeachIndex < teachSprites.Length)
        {
            if (teachImageDisplay != null)
            {
                teachImageDisplay.sprite = teachSprites[currentTeachIndex];
            }
        }
        else
        {
            CloseMenuTeachPanel();
        }
    }

    private void CloseMenuTeachPanel()
    {
        if (teachPanel != null) teachPanel.SetActive(false);
    }

    private void OnExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}