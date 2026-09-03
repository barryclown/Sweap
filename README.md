# Dragame — Sweep 展演 Demo

`Sweep` 是一個以滑鼠甩動掃把、把場上垃圾撞進會自行移動黑洞的 2D 物理爽 Demo。這個版本把重心放在 45～60 秒內「一看就懂、一下就爽、分數能看出操作差異」。

## 操作

| 操作 | 功能 |
|---|---|
| 左鍵拖曳後放開 | 彈射掃把 |
| `E`／滾輪／中鍵 | 切換撞擊、黏附模式 |
| 右鍵按住再放開 | 蓄力清場；滿蓄力有強回饋 |
| `R` | 立即重開本局 |
| `Esc` | 暫停／完整說明 |

第一次進場只會邊玩邊出現三個短提示，不再強制停在兩頁教學。要重播首次提示，可在 Unity 選 `Tools > Sweep Demo > Reset Demo Now`。

## 展演規則

- 舊場景即使仍序列化成 90 秒，執行時也會限制為 45～60 秒。
- 每 15 秒提升一段速度與技能恢復，最後 10 秒進入 `FINAL FEVER`。
- 黑洞自己吸到的垃圾只給原分數 10%，不累積 Combo。
- 玩家用撞擊或黏附推入的垃圾拿完整分數；2.6 秒內連續投入會增加 Combo 倍率，Fever 再乘 1.5。
- 撞擊包含短 hit-stop、鏡頭震動、連撞字樣與音高上升；吸入有爆圈與加分字樣；滿蓄力有全畫面閃光。

## 在 Unity 開啟

指定版本是 Unity `6000.4.0f1`。共用 MCP 自動化入口：

```powershell
python C:\Users\barry\Desktop\claude\unity-template\unity_auto.py prep C:\Fork\Dragame --autosetup
python C:\Users\barry\Desktop\claude\unity-template\unity_auto.py open C:\Fork\Dragame --timeout 480
python C:\Users\barry\Desktop\claude\unity-template\unity_auto.py verify --expect Dragame
```

`prep` 只改檔案、不搶前景；`open` 會開 Unity，Barry 正在工作時應先取得同意。驗收必須是 Unity 端 `execute_code` 真正回覆，不能只以 MCP 工具存在或 8080 有監聽當成功。

## 測試與建置

先正常關閉這個專案的 Unity Editor，再執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\test-demo.ps1
powershell -ExecutionPolicy Bypass -File .\Tools\build-windows.ps1
```

也可以直接從 Unity 選：

- `Tools > Sweep Demo > Run Preflight`
- `Tools > Sweep Demo > Build Windows`

預設輸出是 `Builds\Windows\DragameDemo.exe`，建置／測試日誌在 `Logs\`。腳本偵測到同一專案已開啟時會明確停止，不會強制關閉 Editor。

## 重要程式位置

- `Assets/barry/Sweep/Script/Player/PlayerController.cs`：拖曳、蓄力、模式、hit-stop、鏡頭震動與節奏加速。
- `Assets/barry/Sweep/Script/BaseTrash.cs`：自訂垃圾物理、玩家影響歸因與回收狀態。
- `Assets/barry/Sweep/Script/BlackHoleObstacle.cs`：吸入流程與主動／被動計分。
- `Assets/barry/Sweep/Script/UI/UIManager.cs`：60 秒流程、Combo、Fever、提示與結算。
- `Assets/barry/Sweep/Script/UI/DemoFeedbackOverlay.cs`：執行期展演 HUD，不要求手改 Scene。
- `Assets/Editor/SweepDemoBuild.cs`：Preflight、快速重置與 Windows Build。
- `Assets/Tests/Editor/SweepDemoIntegrityTests.cs`：最小冒煙測試。
