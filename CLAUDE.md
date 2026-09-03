# Unity C# 專案 — Claude Code 架構規範（硬規則）

> Dragame 專案定位：`Sweep` 是 45～60 秒展演用爽 Demo。先讀 `README.md` 與 `AGENTS.md`；優先確保操作、玩家得分因果、撞擊回饋、快速重置。此專案指定 Unity `6000.4.0f1`，不得換版本硬開。

> 本檔只列「可執行硬規則」。每條的**理由/原理**放在同層 `CLAUDE-rationale.md`（不自動載入）；
> 需要時用 Read 查，平常不佔 token。標 `↳R` 者表示 rationale 有對應說明。
> 標 `↳S` 者表示 `CLAUDE-setup.md`（同樣不自動載入）有對應的套件／場景／美術／MCP 詳解，要做該類任務時才 Read。
> 本範本以 **2D 為主軸**：3D 專屬規則會特別標「3D」，其餘 2D/3D 通用。
> **動編輯器前先看十四（Unity MCP）**：這個專案有沒有 MCP 連線，決定三、十、十二、十三怎麼執行——
> 有連線我能自己動場景與 Inspector 並驗證，沒連線只能改 `.cs` 並把驗證交回人類。

## 〇、專案分級（先判定）↳R
- 小品/原型：可**省略** Service Locator 與全套 DI，直接 `GetComponent<T>()` + 輕量 `event`。
- 中大型：才套完整 Service Locator / DI。
- 不分大小：零 GC（七）、YAML 防護（三）、Token 防護（二）一律遵守。

## 一、目標
零 GC 護城河 · 高內聚低耦合 · AI 防護網 · 數據與邏輯分離。

## 二、💰 Token 防護（最高指導原則）↳R
1. 嚴禁無差別全域讀取，精準指定 `.cs` / `.md`。
2. 不讀：`/Library/`、`/Temp/`、`/Obj/`、`/Logs/`、`/Builds/`、影音美術檔（`*.png/jpg/mp3/wav/fbx/obj/mat/anim/controller`）。
   - **單張例外**：自己剛生成的圖要自評、或 MCP 截圖要比對視覺時，可以讀那一張。禁止的是整批掃美術資料夾。
3. 輸出精簡，直接給可替換完整程式碼。
> 註：上述 2 已由 `.claude/settings.json` 的 `permissions.deny` 硬性攔截，非僅靠自律。

## 三、⚠️ Unity 序列化檔（YAML）防護 ↳R
- **純文字編輯 = 絕對禁止**：`*.unity/*.prefab/*.asset/*.mat/*.controller/*.anim/*.meta`（會壞 GUID；已由 settings deny 硬擋 Edit/Write）。
- **唯讀例外**：可用 grep 搜 GUID 確認引用（如刪檔前驗證沒被掛載）。
- **要改場景／Prefab／Inspector 時走這兩條路之一**（↳十四）：
  1. **有 MCP 連線**：走編輯器 API（`manage_gameobject` / `manage_components` / `manage_prefabs` / `execute_code`），由 Unity 自己序列化，GUID 安全 → 我直接做，做完驗證並回報。
  2. **沒有 MCP**：我只出腳本 ＋ 具體步驟清單，「具體指示人類」在編輯器手動完成。
- 判斷有沒有連線**要探測、不要假設**：工具列表看得到不代表 bridge 活著，先發一個唯讀呼叫（如 `read_console`）確認。

## 四、核心信念
1. 增量 > 大爆炸。 2. 實用主義 > 教條。 3. 清晰意圖 > 巧妙代碼。 4. 組合 > 繼承。
5. **3-Strike**：同錯最多修 3 次，未解即停、記錄、回報人類。

## 五、📂 專案結構（禁丟 `Scripts/` 根目錄）
- `Core/`（ObjectPoolManager/SceneLoader/ServiceLocator）· `StateMachine/`（Base/Player/Enemy/Others）
- `Gameplay/`（按實體：Characters/Player、Weapons）🚫 禁巨獸 Controller，拆單一職責
- `UI/` · `Data/`（ScriptableObject）· `Utils/`（GameConstants）· `Interfaces/`（IDamageable…）

## 六、🏗️ 架構
1. 跨領域系統用 `ServiceLocator.Get<T>()`（`Start()` 取），避免 `Instance` 單例。↳R
2. 同實體組件相依：`Awake()` 用 `GetComponent<T>()`，禁 `GameObject.Find()`。
3. 靜態數值強制 ScriptableObject，不寫死、不存 JSON。
4. 跨系統用 `event Action`（零 GC），`OnDestroy()` 必 `-=`。
5. Inspector 變數禁 `public`，用 `[SerializeField] private` + 唯讀屬性。

## 七、⚡ Hot Path 零 GC（`Update`/`FixedUpdate`/`LateUpdate`）
1. 禁 `new`：清單/陣列在 `Awake()` 預配置並重用（`.Clear()`）。
2. 禁 LINQ：改 `for`。
3. **foreach**↳R：✅ 允許具體型別（陣列/`List<T>`/原生集合，零 GC）；🚫 禁迭代 `IEnumerable<T>` 介面（會 box）。極熱內層仍偏好 `for`。
4. 禁高頻字串 `+`，用 `StringBuilder`（給初始容量）。
5. 子彈/特效禁 Runtime `Instantiate/Destroy`，接 `Core/Pooling/`。
6. 物理：剛體邏輯只寫 `FixedUpdate()`；偵測用 `NonAlloc` API + 預配置陣列（3D 用 `Physics.*NonAlloc`+`Collider`/`Rigidbody`；2D 用 `Physics2D.*NonAlloc`+`Collider2D`/`Rigidbody2D`，別混用）。

## 八、🛡️ 命名與防呆
1. 私有 `_camelCase`；公開 `PascalCase`；介面 `I` 開頭。
2. 參考型別呼叫前 Null 檢查（`if (x != null)` / `?.`）。
3. 魔法字串集中 `Utils/GameConstants.cs`。
4. `async/await` 強制傳 `CancellationToken`（如 `destroyCancellationToken`）。↳R

## 九、🔄 生命週期
- `Awake()`：只 `GetComponent<T>()` 抓自身/子節點，禁呼叫外部/定位服務。
- `Start()`：存取外部系統，`ServiceLocator.Get<T>()` / 訂閱外部事件。

## 十、📝 流程與測試
0. **優化/審查任務先探索再動手**：開場先讀懂全貌、畫結構地圖、列風險點（耦合/效能/GC），讓人類挑要動哪個，再進 Plan；不要看到單檔就直接改（治標）。
1. 較大改動前在 `IMPLEMENTATION_PLAN.md` 拆 3–5 階段。
2. 每次改完自查：零 GC 禁令、YAML 寫入防護。
3. **改完必驗證才算完成**：能編譯就編譯、邏輯改動跑對應測試、UI/視覺改動截圖比對，並回報結果；不可改完即宣告完成。
   有 MCP 時的三步驗證循環見十四；沒有 MCP 時要明說「我沒驗到，請你跑一次」，不准把「改完」講成「驗過」。
4. 測試（Unity 需在 PATH 或用完整路徑）：
   - EditMode：`Unity -batchmode -runTests -testPlatform EditMode -projectPath . -testResults ./Logs/EditModeTests.xml -nographics -quit`
   - PlayMode：同上改 `-testPlatform PlayMode`。

## 十一、🧩 套件清單（該裝什麼工具）↳S
1. 動工前先核對 `Packages/manifest.json`：缺的關鍵套件用 Package Manager 補，**不手改版本號亂跳**。
2. **預載核心（每個 2D 專案開案先裝）**：Input System、TextMeshPro、URP（含 2D Renderer / 2D Lights）、2D 基礎（Sprite/Tilemap，2D 範本通常已含）。
3. **選配（該類型才加，不要一律預載）**：Cinemachine（相機跟隨/平台/動作）、2D Pixel Perfect（像素）、DoTween（UI 動效轉場·美術總監核准）、Addressables（大型）、Localization（多語）。
4. 別擅自塞大型第三方套件（Odin 等）；要加先說「用途與原生替代」，barry 認可再裝（DoTween 已核准供 UI 動效）。
> 完整核心/選配表＋套件 id＋安裝法見 `CLAUDE-setup.md` 第一節。

## 十二、🗺️ 場景設定（場景怎麼弄）↳S
1. 場景是 YAML（見三）：**有 MCP** 我用編輯器 API 直接建階層／掛元件／拉引用，做完回報；**沒有 MCP** 我只出腳本＋步驟清單，barry 手動落地。兩種情況都不准純文字編輯 `.unity`。
2. **標準場景**：`MainMenu`（主菜單）、`Game`（主遊戲）；需要再加 `GameOver`/`Result`。
3. **多關卡**：一關一場景，`Level_01`、`Level_02`…（兩位數補零）。
4. **命名**：場景一律 PascalCase；關卡 `Level_數字`；全專案統一，別混大小寫。新場景要加進 Build Settings。
5. 2D URP 別漏：相機 Orthographic、指定 2D Renderer、放 **Global Light 2D**（否則全黑）。
> Bootstrap 常駐場景／Additive 載入是「中大型才用」的選配，細節見 `CLAUDE-setup.md` 第二節。

## 十三、🎨 美術總監（缺圖我來 ＋ 視覺顧問）↳S
兩種模式並存：
- **生成（執行）**：barry 說「缺圖你弄」＝全包到底：定風格→生圖→自評重生→去背/調尺寸/對齊 pivot→放進專案對應夾→做完叫 barry 試玩（不用截圖回報）。**只新增不覆蓋既有資產**，要替換先提案。
- **顧問（建議）**：光影/材質/Shader/UI 排版/構圖的具體參數與除錯清單由我出。**有 MCP** 我自己在編輯器套上去、截圖比對，barry 只驗收；**沒有 MCP** 才退回我給步驟、barry 在 Inspector 落地。

鐵則：
1. 全專案守同一套**風格基準**（解析度/PPU/色板/外框/光源/視角），新資產對齊，禁混風格。
2. **拒絕空泛**：給具體組件名＋建議參數值（如 Bloom Intensity 1.2 / Threshold 0.9），不准只說「加強層次」。
3. **效能意識**：華麗方案必估 Draw Call/GPU 並給優化替代；撞上零 GC/架構硬規則時 **硬規則優先**。
4. 以 **2D 為主軸**；3D 專屬（烘焙 GI/PBR/景深/焦距）標「3D 才用」。
> 4 大領域職責、除錯清單、UI 動效（DoTween）、優先級見 `CLAUDE-setup.md` 第三節。

## 十四、🔌 Unity MCP（有連線＝我能直接動編輯器）↳S
1. **先探測，再動作**：要動編輯器之前先發一個唯讀呼叫（`read_console`）確認連線活著。工具看得到 ≠ bridge 連上；bridge 斷線時每個呼叫都會失敗。
2. **開機順序**：先開 Unity（server 隨 Auto-Start 起來）→ 再開 AI session，MCP 才載入。順序反了就重連或重開 session，別在那邊硬試。
3. **標準驗證循環（改完 C# 的三步）**：`read_console` 確認零編譯錯誤 → `manage_editor` 進 Play Mode 驗行為 → 視覺改動用 `execute_code` 跑 `ScreenCapture.CaptureScreenshot` 截圖比對。這就是十之3 在 Unity 的落地方式。
4. **我能直接做**：場景／物件／元件／Prefab 操作、跑 Test Runner（`run_tests`）、執行菜單項、build。
   **仍然留給人**：Asset Store 匯入、帳號授權與平台簽章、任何不可逆的刪除——這幾件即使有連線也先問。
5. **沒有 MCP＝半盲模式**：只能改 `.cs`，沒 console、沒 Play Mode、沒截圖。此時把驗證責任明確講清楚交回 barry，不要宣稱已驗證（見十之3）。
6. 場景改動前先確認**當前開的是哪個場景**，別在錯的場景上動手；動之前存檔點（git commit）比事後救便宜。
> 三角色架構、Auto-Start 設定、工具清單、Roslyn 依賴、新機器一次性設定腳本、雷點見 `CLAUDE-setup.md` 第四節。

---

<!-- PROJECT-SPECIFIC -->
## 專案特有
> 本區由各專案自行維護：寫這個專案的 Unity 版本、MCP 現況、目錄慣例、特殊限制。
> `install.py --force-claude` 更新範本時**會原樣保留這一區**（靠上面那行 `PROJECT-SPECIFIC` 標記辨識，別刪它）。

- Sweep 展演版驗證順序：`Tools\test-demo.ps1` → `Tools\build-windows.ps1` → `Tools\smoke-player.ps1`。最後一個腳本成功時必須輸出 `SWEEP_SMOKE_OK`；固定種子掛機基準為 16 分，低於 C 的 60 分門檻。
