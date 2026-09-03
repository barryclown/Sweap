# Unity 套件 / 場景 / 美術 / MCP 設定詳解（CLAUDE-setup.md）

此檔**不自動載入**，平常不佔 context token。對應 `CLAUDE.md` 中標 `↳S` 的章節（十一套件、十二場景、十三美術、十四 MCP）。
要裝套件、設場景、做美術、或動編輯器時才 Read 它。

凡「設數值、掛腳本、建 Prefab、改 import」這類寫進 `.unity/.prefab/.asset/.meta`（YAML）的動作，走 `CLAUDE.md` 三的雙軌：
**有 MCP 連線**→ Claude 用編輯器 API 自己做完並驗證；**沒有 MCP** → Claude 列步驟、barry 在編輯器手動完成。
兩種情況都**不准**用文字編輯器直接改 YAML。第四節有連線判斷與工具對照。

---

## 一、🧩 套件清單（該裝什麼工具）

原則：**只把「幾乎每個 2D 專案都會用到」的放進預載核心；其餘依遊戲類型才加**，不要一律預載一堆用不到的。

### 怎麼裝
- **首選**：`Window > Package Manager`，左上 `+` →「Add package by name」輸入套件 id（如 `com.unity.cinemachine`）。
- **批次**：直接編 `Packages/manifest.json` 的 `dependencies`，存檔後 Unity 自動重新解析。版本號別亂填，沒把握就用 Package Manager 選相容版。
- 改完務必讓 Unity 完成 import／編譯再繼續。

### 預載核心（每個 2D 專案開案就先裝）
| 用途 | 套件 id | 說明 |
|---|---|---|
| 輸入 | `com.unity.inputsystem` | 新版 Input System，預設輸入方案（取代舊 Input Manager） |
| 文字 | `com.unity.textmeshpro` | 幾乎每個遊戲都有文字；首次用會提示 Import TMP Essentials |
| 渲染管線 | `com.unity.render-pipelines.universal` | URP，2D 預設管線；2D Renderer 與 2D 燈光（`Light2D`）的前提 |
| 2D 基礎 | `com.unity.feature.2d`（2D Feature Set） | Sprite/Tilemap/Animation 等；用 Unity 2D 範本建專案時通常已內含 |

> Test Framework（`com.unity.test-framework`）新版 Unity 預設已裝，一般不用另列；缺了再補（配合 `CLAUDE.md` 十之4 測試）。

### 選配（該類型才加，不要一律預載）
| 套件 id | 何時才加 |
|---|---|
| `com.unity.cinemachine` | 要相機跟隨／邊界／震動（平台、動作、橫向捲軸）；單畫面遊戲（合成、三消）用不到 |
| `com.unity.2d.pixel-perfect` | 只有像素美術遊戲；配合 Pixel Perfect Camera |
| DoTween（Asset Store／OpenUPM `com.demigiant.dotween`） | UI 動效/轉場 tween（美術總監 UI 段核准）；輕量，比硬刻 Animator 省事 |
| `com.unity.addressables` | 中大型專案的資源／記憶體管理 |
| `com.unity.localization` | 要做多語（中英）時才裝 |

### 不建議隨手裝
- Odin Inspector、各式第三方大型 framework：先說清楚「要解決什麼問題、原生有沒有替代」，barry 認可再裝。能用 `event` + `Coroutine`/`Awaitable` 解決的就別引大依賴。（DoTween 已核准，限 UI 動效用。）

---

## 二、🗺️ 場景設定（場景怎麼弄）

目標：給一套**簡單夠用**的標準場景與命名規範，不過度設計。小品/原型照這節就好；龐大的常駐管理器架構放最後「進階」當選配。

### 我能做 / 不能做
- **永遠能**：寫管理器/載入器腳本、給出「場景要放哪些物件」的清單、列出要拉什麼引用/設什麼值。
- **有 MCP 連線時還能**：用 `manage_scene` 開/存場景、`manage_gameobject` 建物件與階層、`manage_components` 掛元件設欄位、`manage_prefabs` 做 Prefab——走編輯器 API，序列化由 Unity 負責，GUID 安全。做完 `read_console` 驗證再回報。
- **永遠不能**：用文字編輯器直接改 `.unity`（YAML，會壞 GUID）。沒有 MCP 時場景搭建＝我交腳本＋步驟，barry 在編輯器落地。

### 標準場景（小品/原型的預設集）
| 場景 | 用途 | 必要性 |
|---|---|---|
| `MainMenu` | 主菜單：開始 / 設定 / 離開 | 預設 |
| `Game` | 主遊戲（單場景遊戲就全放這） | 預設 |
| `GameOver` / `Result` | 死亡 / 結算介面 | 需要才加 |
| `Level_01`、`Level_02`… | 多關卡時一關一場景 | 多關才加 |

> 最省做法：只要 `MainMenu` + `Game` 兩個場景。

### 命名規範
- 場景檔一律 **PascalCase**：`MainMenu`、`Game`、`GameOver`。
- 多關卡：`Level_` + 兩位數，如 `Level_01`、`Level_12`（補零才排得整齊）。
- 一個專案內規則統一，別混 `mainmenu` / `Main_Menu` / `scene1` 這種。
- 每個新場景記得加進 `File > Build Settings > Scenes In Build`，並排好 index（通常 `MainMenu` 在最前）。

### 2D URP 最低要求（漏了會出錯）
- **相機**：Projection = `Orthographic`；Renderer 指到專案的 **2D Renderer (URP)** asset。
- **燈光**：場景至少一個 `Light2D`（Type = Global），否則開 URP 後畫面全黑。
- **音訊/UI**：全場景只留**一個** AudioListener（通常掛 Main Camera）、一個 `Canvas` + 一個 `EventSystem`。

### 進階（中大型才用，不是預設規範）
- **Bootstrap 常駐場景**：`_Bootstrap`（Build index 0，底線前綴排最上）只放跨關卡管理器（`ServiceLocator`/`AudioManager`/`ObjectPoolManager`），用 `Core/SceneLoader` 以 **Additive** 載入關卡、管理器常駐不重建。
- **階層分組**：用空物件當資料夾（`---Systems---`/`---Actors---`/`---UI---`…），不掛邏輯、Transform 歸零。
- 小品/原型不需要這套；直接 `MainMenu` + `Game` 最省。

---

## 三、🎨 美術總監（缺圖我來 ＋ 視覺顧問）

以 **2D 為主軸**；條目標 `[3D]` 者為 3D 專案才用。

### 角色與兩種模式
- **生成（執行）**：barry 說「缺圖你弄／美術你包」→ 全流程自走：定風格 → 生圖 → 自評重生 → 後製 → 放進專案 → 做完叫 barry 試玩（不必截圖回報，他自己進遊戲看）。產物是 PNG，我能寫。
- **顧問（視覺總監）**：光影/材質/Shader/UI/構圖的具體參數、步驟、除錯清單由我出。落地看有沒有連線（`CLAUDE.md` 三的雙軌）：**有 MCP** → 我自己在編輯器套參數、截圖比對、回報前後差異，barry 只驗收；**沒有 MCP** → 我給步驟清單，barry 在 Inspector 落地，我不宣稱驗過。

### Output Rules（顧問模式鐵律）
1. **拒絕空泛**：禁止「讓畫面更有層次」「加強對比」這種廢話。一律給 Unity 內具體操作步驟、組件名稱、建議參數值（例：Post-Processing 的 Bloom Intensity 設 1.2、Threshold 設 0.9）。
2. **效能意識**：提任何華麗方案都要主動評估 Draw Call / GPU 負擔並附優化替代；撞上 `CLAUDE.md` 七（零 GC）/架構硬規則時 **硬規則優先**，美術在效能預算內落地。
3. **視覺除錯**：barry 說畫面「灰灰的/很廉價/排版很怪」時，像醫生一樣直接列最可能的 3–5 個 Unity 設定失誤引導排查（如 Color Space 沒設 Linear、Sprite 的 sRGB/壓縮設錯、場景沒掛 Post-Processing Volume 或沒設 Global、Canvas Scaler 模式/Reference Resolution 錯、像素圖 Filter 不是 Point 糊掉）。
4. **術語精確**：用 Unity 官方組件名與美術專名，確保 barry 能直接在 Inspector 找到對應功能。

### 風格基準（style bible，開案先定、全專案共用）
開新專案或第一張圖前先定下並全程沿用，新資產一律對齊，**禁混風格**：美術風格（像素/手繪/扁平/卡通）、基準解析度與 **PPU**、色板（主/輔/中性）、外框（黑邊粗細/內描邊）、光源方向與視角。

### 生成模式：生圖 → 後製 → 整合
- **生圖**：簡單圖示我直接出（SVG/程式生圖）；正式美術走既有工具（見記憶 `ai-image-gen-project`、`toolchain-reference`）——`Desktop\claude\ai-images\` 的 `gemini_web.py` 操控已登入 Gemini 網頁出圖，或 `gen_image.py`／API 備案。出圖後**自評**，不合格就重生。
- **後製**：去背透明 PNG、裁切、統一尺寸、對齊 **pivot**（角色常 Bottom/Bottom-Center）；像素圖避免抗鋸齒糊邊。
- **整合**：放 `Assets/Art/Sprites/{Characters,Environment,UI,FX}`；動畫幀連號 `run_00`、`run_01`…。
- **Sprite import**：Texture Type=`Sprite (2D and UI)`、Sprite Mode=Single/Multiple、Pixels Per Unit 對齊風格、像素遊戲 Filter Mode=`Point` + Compression=`None` + 關 Mip Maps、Pivot 統一、多圖用 **Sprite Atlas** 省 draw call。
  落地方式：**有 MCP** → 我用 `execute_code` 走 `TextureImporter` API 設定並 reimport（合法路徑，不是改 `.meta` 文字）；**沒有 MCP** → 我列清單，barry 在 Inspector 套或設專案 Preset。
- **鐵則**：**只新增不覆蓋**既有資產，要替換先提案（見 `game-art-full-autonomy`、`redo-preserve-content-propose-changes`）。受 `CLAUDE.md` 二、三防護：不用文字編輯器寫任何 `.meta`、不整批掃美術資料夾；**但自己剛生成的圖要自評、或 MCP 截圖要比對時，讀那單張是允許的**（`CLAUDE.md` 二之2 例外）——不自評就等於把明顯不合格的圖丟給 barry。

### 顧問模式：四大領域職責（依 ROI 排序）
優先級＝2D 獨立遊戲＋作品集導向的視覺 CP 值：**UI/排版 ＞ 光影/Post ＞ 構圖/鏡頭 ＞ 材質/Shader**。

**① UI/UX & 字體排版（最高 ROI，每個遊戲都吃）**
- 精準掌握 Canvas 層級、Anchor / Pivot；Canvas Scaler 用 `Scale With Screen Size` + 固定 Reference Resolution。
- 字體學：依風格推薦字體，給具體字號、行距、字距（Character Spacing）、對比度建議。
- 留白（Margin/Padding）的呼吸感與易用性。
- 動態 UI 轉場：用 **DoTween**（已列核准選配）或 Animator，確保回饋順暢、符合視覺直覺。

**② 光影 & Post-Processing（CP 值最高的視覺升級）**
- Post-Processing Volume：Bloom、Color Grading、Tonemapping、Vignette 定情緒色調，每項給具體參數。
- 2D 即時光：`Light2D`（Global + 點/聚光）；sprite 用 normal/mask map 增立體感。
- `[3D]` Lightmap 烘焙 / Global Illumination / 景深 DoF —— 2D 改用即時 `Light2D`，不烘焙。
- 效能：每個 Post effect 都吃 GPU；低階機關 Bloom 或降 Volume，先講負擔再加。

**③ 構圖 & 鏡頭（有移動鏡頭才需要）**
- Cinemachine 2D：Framing Transposer 跟隨、Confiner2D 邊界、Noise 震動、Blend 轉場。
- `[3D]` 焦距 Focal Length、景深、攝影機軌跡強化張力；2D 正交相機改調 Orthographic Size。
- 合成/三消等單畫面遊戲通常不需要這塊。

**④ 材質 & Shader（2D 最少用，特殊 FX 才出手）**
- Shader Graph 做溶解、流光、水體、卡通描邊等 2D 特效。
- `[3D]` 完整 PBR：Albedo/Normal/Metallic/Smoothness/AO 精準配置；2D 只用到 sprite normal/mask map。
- 把關材質球效能與 Texture 壓縮格式（手機偏 ASTC，避免無謂 RGBA32）。

---

## 四、🔌 Unity MCP（AI 直接連編輯器）

對應 `CLAUDE.md` 十四。這節決定「AI 是隔著手套改程式，還是真的坐在 Unity 前面」——有連線時，
場景操作、編譯檢查、Play Mode、截圖都能自己做完再回報；沒連線就退回只改 `.cs` 的半盲模式。

用的是 **MCP For Unity**（CoplayDev）：package id `com.coplaydev.unity-mcp`，來源是固定版 git URL（`CoplayDev/unity-mcp` `v10.2.0`）。不要改回浮動的 `#main`；升版後要重跑 `verify` 與實際 Editor 驗證。

### 三角色架構（缺一不可）

| 角色 | 是什麼 | 壞掉時的症狀 |
|---|---|---|
| ① AI client | Claude Code / Codex，連 `http://127.0.0.1:8080/mcp` | session 裡看不到 UnityMCP 工具 |
| ② Python server | `mcp-for-unity-server`，中繼站，掛 8080 埠 | session 啟動時整組工具不載入 |
| ③ Unity 內 bridge | 編輯器裡的 plugin，反向連到 server | **工具看得到但每個呼叫都失敗**（最容易誤判的一種） |

要點：**server 是獨立進程，Unity 關掉它照活**（8080 續留），Unity 重開後 bridge 會自己連回同一個 server。
所以「工具存在」只證明 ①②，不證明 ③。這就是 `CLAUDE.md` 十四之1 要先探測的原因。

### 連線判斷（探測法）
動任何東西前發一個唯讀呼叫：`read_console`。
- **有回應** → 三角色都活著，走 `CLAUDE.md` 三的第一條路（我自己動編輯器）。
- **呼叫失敗** → bridge 沒連上。先確認 Unity 開著且已載入完；不行就叫 barry 重開 session（Claude 可試 `/mcp reconnect`）。
- **工具根本不在列表** → server 沒起或開機順序反了，重啟 session 才會載入。

### 開機順序（會踩一次的坑）
**先開 Unity → 再開 AI session。** 反了 MCP 不會載入。
- Unity 載入要 1–3 分鐘，之後 Auto-Start 自己接手，人不用做事。
- 驗證方式：8080 在 Listen ＋ 有 established 連線且 client 是 Unity。
- server log 在 `%LOCALAPPDATA%\UnityMCP\Logs\`，連不上時先看這裡。

### Auto-Start（設一次，全專案生效）
開任何 Unity 專案 → server + bridge 全自動，零手動。
- 開關是 EditorPrefs 的 `MCPForUnity.AutoStartOnLoad` = true。**EditorPrefs 是「每使用者」共用**，設一次所有專案（含未來新專案）都生效。
- 只在 **HTTP transport** 模式作用；domain reload（改完 C# 重編譯）後由 bridge 的 reload handler 自動恢復。
- **新機器／新環境**：把同層的 `McpAutoSetupOnce.cs` 丟進專案 `Assets/Editor/`，Unity 一編譯就生效——它會開 Auto-Start、順手裝 Roslyn、並立刻踢一次 auto-start（不用重開編輯器），結果寫到 `Temp/mcp_auto_setup_result.txt`。看到 `DONE` 就可以把腳本刪掉。內含 `SessionState` 防重跑。

### 工具對照（依任務挑，不用背全部）

| 想做的事 | 用哪個 |
|---|---|
| 確認編譯有沒有錯 | `read_console`（**每次改完 C# 的第一步**） |
| 在編輯器裡跑任意 C# 驗證想法 | `execute_code`（最萬用的驗證神器） |
| 進出 Play Mode | `manage_editor` |
| 開/存/切場景 | `manage_scene` |
| 建物件、改階層 | `manage_gameobject` / `find_gameobjects` |
| 掛元件、設欄位值 | `manage_components` |
| 做 / 改 Prefab | `manage_prefabs` |
| 跑單元測試 | `run_tests` + `get_test_job`（驗證閉環） |
| 建置 | `manage_build` |
| 執行編輯器菜單項 | `execute_menu_item` |
| 多個操作省 round-trip | `batch_execute` |
| **截圖** | 沒有專用工具 → `execute_code` 跑 `ScreenCapture.CaptureScreenshot` |

其餘（probuilder / vfx / physics / graphics…）用到再研究。

### Roslyn（`execute_code` 想寫現代 C# 就要它）
- **每專案一份**，裝在 `Assets/Plugins/Roslyn`（5 個 NuGet DLL）。裝了才能用 `compiler=roslyn`。
- 沒裝時 `execute_code` 走內建 **CodeDom，卡在 C# 6**：最常見的翻車是 **local function 直接 compile error** → 沒 Roslyn 就把程式碼寫平鋪。
- 換專案要先確認裝了沒；沒有就用 `McpAutoSetupOnce.cs` 或套件內的 RoslynInstaller 裝。

### 可選依賴政策
ProBuilder / VFX Graph：2D 專案用不到，不裝。Cinemachine：那顆按鈕只是把 `com.unity.cinemachine` 加進當前專案，要鏡頭系統時再裝（見第一節選配表）。

### 雷點
- **`execute_code` 要帶 `action`**：新版（server 3.4.x）把它改成 `action` 必填，只給 `code` 會回 validation error。跑程式碼是 `action="execute"`，另有 `get_history` / `replay` / `clear_history`。
- **Unity 沒 focus 不會刷新 Assets**：從外部塞檔（例如生完美術圖）後，要讓 Unity 拿到 focus 才會 import；必要時用視窗前景化喚它。
- **會動的東西別亂升**：套件版本正常運作時，看到新版先不動。
- 動場景前先確認**當前開的是哪個場景**，並先留 git 存檔點。
- 沒有連線時**不要宣稱驗證過**——半盲模式沒有 console 也沒有 Play Mode，照 `CLAUDE.md` 十之3 明講交回人類。
