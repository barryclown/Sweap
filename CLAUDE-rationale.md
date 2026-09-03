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

## 六之1、Service Locator — 為什麼取代 Singleton
裸 Singleton（`Manager.Instance`）讓任何腳本都能隨處抓全域狀態，依賴關係散落、難測試、初始化順序易爆。Service Locator 把「取得服務」集中在一個註冊點，至少讓依賴有跡可循、可在測試時替換 mock。（但見〇的 anti-pattern 提醒：小專案不必。）

## 七之3、foreach — 為什麼不是全禁
這是原稿最常見的誤解。現代 Unity/C#：
- `foreach` 跑**具體型別**（`T[]`、`List<T>`、原生集合）用的是 **struct enumerator**，在堆疊上、**零 GC 分配**。把它改成 `for` 不會省到任何 GC，只是讓程式更囉嗦、可讀性更差。
- 只有 `foreach` 跑**`IEnumerable<T>` 介面型別**時，enumerator 會被 box 成 reference type → 產生堆積垃圾 → 觸發 GC。
所以正確規則是「禁介面迭代，不禁具體型別」。一刀切全禁是技術上錯的，且違反「實用主義」。極熱內層迴圈仍偏好 `for` 是另一個理由：可索引、最直觀、好除錯，與 GC 無關。

## 八之4、CancellationToken — 為什麼強制傳
`async/await` 任務若在 GameObject 銷毀後還在跑，會存取已死物件 → NullReference 或詭異狀態。傳入 `destroyCancellationToken`（Unity 內建，物件銷毀時自動取消）讓任務在物件死掉時乾淨中止，避免「殭屍任務」。
