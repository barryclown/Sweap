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
powershell -ExecutionPolicy Bypass -File .\Tools\smoke-player.ps1
powershell -ExecutionPolicy Bypass -File .\Tools\stress-collisions.ps1
```

也可以直接從 Unity 選：

- `Tools > Sweep Demo > Run Preflight`
- `Tools > Sweep Demo > Build Windows`

預設輸出是 `Builds\Windows\DragameDemo.exe`，建置／測試日誌在 `Logs\`。`smoke-player.ps1` 會以固定亂數種子、不開視窗地加速跑完一局，並輸出掛機分數／評級；2026-09-05 的基準結果是 `score=14, grade=None`，低於 C 的 60 分門檻。腳本偵測到同一專案已開啟時會明確停止，不會強制關閉 Editor。

`stress-collisions.ps1` 會在正式 Player 中把 12 個垃圾壓成密集團並施加高力度，確認測試確實產生速度與位移，再驗證它們能分離、保持有限數值，且不穿出主邊界或場景障礙；只在帶 `-sweepCollisionStress` 參數時啟動，不影響正常遊戲。

## 碰撞與場景自動化

新增或更換垃圾 Sprite 後，不必再手動畫碰撞體：

- `Tools > Sweep Demo > Collision > Auto-Fit All Trash Prefabs`：從 Sprite alpha 輪廓建立最多 6 個串珠節點及 16 點 PolygonCollider2D。
- `Tools > Sweep Demo > Collision > Validate All Trash Prefabs`：檢查 16 個 Prefab 的漏判、外溢、節點與 Polygon。
- `Tools > Sweep Demo > Collision > Set Up Scene Footprints and Bottom Wall`：重算人物／桌子／燈具的 2.5D 接地區，並建立純視覺下牆。
- `Tools > Sweep Demo > Project > Organize Safe Active Assets`：以 AssetDatabase 保留 GUID 地整理使用中的資料夾與名稱。

垃圾互撞使用 `BaseTrash` 的串珠節點；掃擊、吸取及黑洞 Trigger 使用 PolygonCollider2D，兩者由同一份 alpha 輪廓產生。玩家本體、技能範圍、黑洞吸取半徑及主世界邊界屬手感／規則參數，不做 Sprite 自動擬合。下邊物理原本已由 `WorldBounds2D` 和 EdgeCollider 提供，因此新增的 `BottomWallVisual` 刻意不帶 Collider，避免雙重反彈。

## 重要程式位置

- `Assets/barry/Sweep/Scripts/Player/PlayerController.cs`：拖曳、蓄力、模式、hit-stop、鏡頭震動與節奏加速。
- `Assets/barry/Sweep/Scripts/BaseTrash.cs`：自訂垃圾物理、玩家影響歸因與複合碰撞解算。
- `Assets/barry/Sweep/Scripts/BlackHoleObstacle.cs`：吸入流程與主動／被動計分。
- `Assets/barry/Sweep/Scripts/UI/UIManager.cs`：60 秒流程、Combo、Fever、提示與結算。
- `Assets/barry/Sweep/Scripts/UI/DemoFeedbackOverlay.cs`：執行期展演 HUD，不要求手改 Scene。
- `Assets/Editor/SweepColliderAutoFit.cs`：垃圾 alpha 輪廓、Polygon 與串珠自動擬合。
- `Assets/Editor/SweepSceneCollisionSetup.cs`：2.5D 接地碰撞與四面視覺牆設定。
- `Assets/Editor/SweepProjectOrganizer.cs`：可重跑、GUID 保留式安全整理。
- `Assets/Editor/SweepDemoBuild.cs`：Preflight、快速重置與 Windows Build。
- `Assets/Tests/Editor/SweepDemoIntegrityTests.cs`：最小冒煙測試。
- `Tools/smoke-player.ps1`：正式 Windows Player 的無畫面整局冒煙測試。
