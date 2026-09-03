# Dragame / Sweep AI 協作規則

- 專案根目錄：`C:\Fork\Dragame`；固定使用 `ProjectSettings/ProjectVersion.txt` 指定的 Unity `6000.4.0f1`，不要用其他版本硬開。
- 這是 45～60 秒的「展演用爽 Demo」。優先順序是操作可靠、玩家因果清楚、撞擊回饋與快速重置，而不是擴充地圖或長線內容。
- 場景、Prefab、`.asset`、`.meta` 不可直接改 YAML。需要掛元件、設序列化欄位或存場景時，使用 Unity MCP／Editor API，完成後再讀 Console、跑測試並實玩驗證。
- Unity MCP package 為 `com.coplaydev.unity-mcp`；客戶端連線是 `http://127.0.0.1:8080/mcp`。只看到工具或 8080 開啟不算成功，必須讓 `execute_code` 回傳目前專案名稱。
- 共用自動化入口：`python C:\Users\barry\Desktop\claude\unity-template\unity_auto.py prep|open|new|verify`。`open`／`new` 可能搶前景；Barry 正在使用電腦時先問，只執行 `prep` 不需要問。
- 冒煙測試：`powershell -ExecutionPolicy Bypass -File .\Tools\test-demo.ps1`。
- Windows 建置：`powershell -ExecutionPolicy Bypass -File .\Tools\build-windows.ps1`。
- Player 整局冒煙：建置後跑 `powershell -ExecutionPolicy Bypass -File .\Tools\smoke-player.ps1`；成功必須出現 `SWEEP_SMOKE_OK`，目前固定種子掛機基準是 16 分、低於 C 門檻。
- 同一專案已在 Unity 開啟時，不啟第二個 batchmode Editor；正常關閉後才測試／建置，也不要強殺可能含未存 Scene 的 Editor。
