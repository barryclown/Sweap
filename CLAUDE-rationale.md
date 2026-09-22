# CLAUDE.md 規則理由（rationale）

此檔**不自動載入**，平常不佔 context token。Claude 只在 barry 問「某條為什麼」時才 Read 它。
對應 `CLAUDE.md` 中標 `↳R` 的條目。

---

## 〇、專案分級 — 為什麼要分大小
完整 Service Locator + DI + 全 ScriptableObject + 物件池是「中大型專案」的規格。小品/原型硬套，重構成本常**大於**收益，違反「實用主義 > 教條」。小遊戲直接 `GetComponent` + 輕量 `event` 就夠乾淨。
另註：Service Locator 本身在學界常被視為 anti-pattern——它只是把「全域 Singleton」換成「隱藏式全域依賴」，依賴關係不顯眼、較難測。它不是銀彈，是「比裸 Singleton 好管」的折衷，視規模權衡。

## 二、Token 防護 — 為什麼這麼嚴
- `Library/`、`Temp/` 是 Unity 的編譯快取，可達**數 GB**。AI 一旦去讀，token 是天文數字的浪費，且內容對改程式毫無幫助。
- 影音/美術二進位檔（png/fbx/wav…）讀進來是亂碼，純燒錢。
- **散文 vs 硬擋**：光在文件寫「禁止讀取」只是提醒，能不能擋 100% 靠 AI 自律。真正可靠的是 `.claude/settings.json` 的 `permissions.deny`——由 Claude Code 程式本體在工具執行前攔截，AI 想讀也被拒。本範本已把這些 deny 規則自動裝進專案。

## 三、YAML 防護 — 為什麼寫入絕對禁、讀取可例外
- **寫入禁**：`.unity/.prefab/.meta` 等是 YAML 序列化檔，靠 GUID 互相參照。AI 改它極易破壞結構或對錯 GUID，導致 Unity 開不了專案、引用全斷，災難性且難復原。所以這類「設數值、掛腳本、建 Prefab」一律請人類在編輯器手動做。
- **唯讀 grep 例外**：刪一支腳本前，要確認它沒被任何場景/prefab 掛載——這需要搜該腳本 `.meta` 的 GUID 是否出現在其他 YAML。grep GUID 成本極低、不 dump 整檔，且能避免「盲刪」風險。全禁讀反而更危險。

## 三之補、為什麼「MCP 能動場景」不等於「放寬 YAML 禁令」
這兩件事的差別是**誰在寫檔**，不是「規則鬆了」。
- **文字編輯 `.unity`**：AI 直接生成／修改 YAML 文字。它得自己算對 GUID、fileID、序列化欄位順序，錯一個字元就可能讓 Unity 開不了專案，而且錯誤往往延遲爆發、難回溯。這條永遠禁，也已由 settings deny 硬擋。
- **走 MCP 編輯器 API**：AI 呼叫的是 `GameObject`/`Component` 這層的編輯器 API，**真正寫檔的是 Unity 自己**——GUID、引用、序列化格式都由 Unity 維護，和人在 Inspector 點一樣安全。
所以原本「一律指示人類手動做」不是因為「AI 碰場景很危險」，而是因為**當時沒有安全的手**。有了 MCP 就有了安全的手，該退回人類的只剩不可逆與需要帳號授權的操作。

## 十四之1、為什麼要先探測連線，不能假設
MCP 是三段接線（AI client → Python server → Unity 內 bridge），而**工具清單只反映前兩段**。
Unity 沒開或 bridge 斷線時，工具依然「看得到」，但每個呼叫都會失敗。若不先探測就規劃一串編輯器操作，會走到一半才發現全做不了，白燒 token 又得重排計畫。
一個 `read_console` 成本極低，卻能一次確認三段都活著——這是最便宜的保險。

## 十四之5、為什麼半盲模式一定要明講
沒有 MCP 時，AI 看不到 console、進不了 Play Mode、也截不了圖，「改完」和「能跑」之間完全沒有證據。
此時若照常回報「已完成」，人類會以為驗過了而跳過檢查，錯誤就一路帶到後面才爆。
所以規則是：**沒驗到就說沒驗到**，並明確講出要人類跑哪一步。這條和「改完必驗證」是同一個精神——不讓未驗證的東西偽裝成已驗證。

## 十五之1、Git — 為什麼本機 commit 直接授權
- 舊寫法只有十四之6 一句「動之前存檔點比事後救便宜」，是建議語氣、又只講場景。Claude Code 本身的預設是「沒被要求就不 commit」，兩者一疊，AI 每次都在等人同意，結果是**根本沒有存檔點**：一個專案三個多月沒 commit（63 項改動、含整套新系統與 6 個場景）；另一個 119 個檔拖到發版前才第一次進版控。
- 本機 commit 隨時能退回，也不會對外，風險接近零；真正會丟東西或對外的是 push、改寫歷史、丟棄改動，所以只有這些要先問。
- 其中「一條指令就能清掉所有沒 commit 的東西」那幾條（`reset --hard`、`clean`、整棵 checkout／restore）再加 settings deny 硬擋：「先問」靠 AI 自律，deny 由 Claude Code 在執行前攔截。常 commit 之後誤跑的損失只剩最後一段，但這層保險成本幾乎是零。
- 單人專案不開分支：分支是給多人並行或長期實驗用的，單人小品每次都開分支只會多出一堆沒人合併的線。

## 十五之4、為什麼截圖不放專案裡
- 放在 `Assets/` 底下會被 Unity 當貼圖匯入（每張長 `.meta`、進 import 流程），又容易被一起 commit——曾經累積到 54 張、14 MB 的診斷圖。
- 截圖是「驗證紀錄」不是「遊戲素材」，放桌面專用資料夾，跟專案生命週期分開，專案刪了截圖還在。

## 六之1、Service Locator — 為什麼取代 Singleton
裸 Singleton（`Manager.Instance`）讓任何腳本都能隨處抓全域狀態，依賴關係散落、難測試、初始化順序易爆。Service Locator 把「取得服務」集中在一個註冊點，至少讓依賴有跡可循、可在測試時替換 mock。（但見〇的 anti-pattern 提醒：小專案不必。）

## 七之3、foreach — 為什麼不是全禁
這是原稿最常見的誤解。現代 Unity/C#：
- `foreach` 跑**具體型別**（`T[]`、`List<T>`、原生集合）用的是 **struct enumerator**，在堆疊上、**零 GC 分配**。把它改成 `for` 不會省到任何 GC，只是讓程式更囉嗦、可讀性更差。
- 只有 `foreach` 跑**`IEnumerable<T>` 介面型別**時，enumerator 會被 box 成 reference type → 產生堆積垃圾 → 觸發 GC。
所以正確規則是「禁介面迭代，不禁具體型別」。一刀切全禁是技術上錯的，且違反「實用主義」。極熱內層迴圈仍偏好 `for` 是另一個理由：可索引、最直觀、好除錯，與 GC 無關。

## 八之4、CancellationToken — 為什麼強制傳
`async/await` 任務若在 GameObject 銷毀後還在跑，會存取已死物件 → NullReference 或詭異狀態。傳入 `destroyCancellationToken`（Unity 內建，物件銷毀時自動取消）讓任務在物件死掉時乾淨中止，避免「殭屍任務」。

## 六之3、靜態數值 — 為什麼 ScriptableObject 是預設，JSON 是例外而不是禁令
（這條原本寫「強制 ScriptableObject，不寫死、**不存 JSON**」。2026-09-09 改掉：那個「不存 JSON」是沒附理由的絕對禁令，而且跟〇分級自相矛盾——〇 說小品硬套全 ScriptableObject 收益小於成本，六.3 卻寫強制。）

**ScriptableObject 適合當預設**：型別安全、Inspector 可編、能直接引用其他資產（Prefab／Sprite／AudioClip）、不必解析、改了會進版控 diff。這幾件事 JSON 做不到或做得比較差，所以「作者端的權威資料」放 SO 是對的。

**SO 做不到的只有一件事，但那件事很重要**：Build 之後就烤死了。要讓不開 Unity 的人（企劃、測試、玩家、面試官）改數值，或想在遊戲執行中邊玩邊調，SO 幫不上忙。這時 JSON 覆寫層是正解——不是違反規則，是用對工具。

**覆寫層的紀律**（照這個做才不會失控）：
- 預設值留在 SO／Inspector／程式碼，JSON 只覆寫它「有寫到」的欄位。
- 整份 JSON 刪掉，行為要能回到原狀——這既是驗收條件，也是不敢亂改時的退路。
- 壞掉的 JSON 不該讓遊戲炸掉，退回預設並警告就好。

### 實作紀錄：VaniaCastle 數值熱重載（2026-09-09）
兩個踩過才知道的點：

**① 兩種數值要走不同路徑，別想用同一招打完。**
狀態機裡的 const（`dashSpeed`、`chaseSpeed`…）改成 `private static float chaseSpeed => GameTuning.Skeleton.chaseSpeed;`＝每幀直接讀，數值一換就跟著變，不必做任何推送。
Inspector 型欄位（`jumpForce`、`maxHealth`、Boss 那一大包）沒辦法這樣寫，得在檔案變動時用 `ApplyToScene()` 主動推回元件。
把這兩種混為一談的話，不是白寫一堆推送程式碼，就是改了 JSON 只有一半生效。

**② `JsonUtility` 分不出「欄位不存在」和「欄位是 0」。**
`FromJsonOverwrite` 對缺席欄位的行為是「保持原值」，聽起來正好，但你沒辦法問它「這個欄位到底有沒有出現過」。
於是 `"jumpForce": 0` 和「JSON 根本沒寫 jumpForce」在程式眼中一模一樣——前者是企劃真的想把跳躍關掉，後者應該完全不要碰 Inspector。
解法是預設值填哨兵（`float.NaN` / `int.MinValue`），用 `Has()` 判斷該不該覆寫；`JsonUtility` 不支援 nullable，這是最直白的替代。
少了這一層，「JSON 沒寫的欄位不覆寫 Inspector」這個承諾就是假的，而那正是覆寫層敢用的前提。

## 六之5、Inspector 變數 — 為什麼從「禁 public」改成「新寫的這樣寫」
`[SerializeField] private` + 唯讀屬性確實比 `public` 好：外部改不到、封裝完整、重構安全。
但寫成「禁 public」在既有專案裡是空話——VaniaCastle 實測 90 個 `public` 欄位對 47 個 `[SerializeField] private`，違反次數是遵守的兩倍。
一條全專案都在違反的規則，實際效果不是讓程式變好，是讓 AI 每次讀到都想順手發動大重構，或乾脆學會忽略整份守則。
所以改成「新寫的照這條、改到哪順手收哪個」——這是做得到的版本。
