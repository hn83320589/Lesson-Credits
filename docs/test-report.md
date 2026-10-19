# 測試報告 — Lesson Credits

| 項目 | 內容 |
|---|---|
| 測試日期 | 2026-10-19（台灣時間 19:18 ～ 19:25） |
| 測試環境 | macOS、.NET SDK 10.0.402、EF Core 10.0.13、SQLite |
| 測試範圍 | 題目所列全部 15 項驗收標準、交付物、技術限制 |
| 結論 | **15 / 15 項驗收標準通過**；驗收時發現 3 個小瑕疵，兩輪程式碼審查後修正 3 個帳務問題並強化併發測試（見第 5、7 節），皆已補測試並重新驗證 |

## 1. 測試方法

| 層級 | 方式 | 說明 |
|---|---|---|
| 單元測試 | `dotnet test`（xUnit） | 規則層為純函式；服務層使用 SQLite in-memory + `FakeTimeProvider`，可精準控制「上課前 24 小時 1 分」等邊界 |
| 端對端測試 | Playwright 操作瀏覽器 | 把程式碼複製到乾淨的目錄，以全新資料庫啟動（port 5299），用店長與老師帳號實際操作畫面 |
| 資料一致性檢查 | SQL | 對範例資料與端對端操作後的整個資料庫，逐條檢查業務規則是否成立 |
| 獨立計算比對 | SQL | 月報與提醒名單以另一套 SQL 重算，和畫面數字比對 |

## 2. 驗收標準結果

| # | 驗收標準 | 單元測試 | 端對端驗證 | 結果 |
|---|---|---|---|:-:|
| 1 | `dotnet run` 後自動建立資料庫並載入範例資料，可用店長與老師帳號登入 | — | 全新目錄啟動後自動套用 migration、建立 `lessoncredits.db`、載入範例資料；`owner`、`chen` 皆登入成功 | ✅ |
| 2 | 購買 10 堂後剩餘點數 +10，到期日為購買日 + 6 個月 | `Buying_ten_lesson_plan_adds_ten_credits_expiring_in_six_months`、`ExpiryDate_*` | 王柏翰購買 10 堂方案：10 → **20 點**，購買日 2026-10-19，到期日 **2027-04-19** | ✅ |
| 3 | 出席扣 1、缺席扣 1、請假不扣，並有單元測試 | `Present_or_absent_deducts_one_credit`（出席、缺席）、`On_time_leave_does_not_deduct_and_grants_makeup_credit`、`Deducts_credit_only_for_regular_present_or_absent` ×5 | 張家豪出席 12 → **11**；王柏翰缺席 20 → **19**；全資料庫 25 筆請假紀錄淨扣點皆為 0（檢查 #2） | ✅ |
| 4 | 上課前 24h1m 請假產生補課資格；23h59m 視為缺席，並有單元測試 | `Leave_requested_24h01m_before_class_is_on_time`、`Leave_requested_23h59m_before_class_is_late`、`Leave_requested_exactly_24h_before_class_is_on_time`、`Late_leave_cannot_be_recorded_as_leave_and_absence_deducts` | 實際時鐘下：提前 24h15m 請假 → **準時**；提前 23h45m → **逾時，視為缺席**（截圖 `e2e-late-leave.png`）。沒有準時請假紀錄時點「請假」會被拒絕並說明原因 | ✅ |
| 5 | 停課日場次全部取消，該日沒有任何扣點紀錄 | `Closure_day_cancels_all_sessions_that_day_and_no_credits_are_deducted`、`Closure_day_is_skipped_when_generating_sessions` | 畫面標記 10/24 停課：當天 2 個場次 → **Cancelled**，2 筆補課預約退回「可使用」；3 個停課日的扣點紀錄 **0 筆**。已有點名的日期不允許停課 | ✅ |
| 6 | 補課資格超過 60 天無法使用；小班已滿 4 人時無法預約，並有單元測試 | `Makeup_credit_older_than_60_days_cannot_be_used`、`Makeup_credit_is_valid_on_day_60_but_not_day_61`、`Expired_makeup_credit_cannot_be_booked_even_for_an_earlier_session`、`Cannot_book_makeup_into_a_full_group_class`、`Makeup_bookings_count_towards_capacity` | 逾期資格顯示「已於 2026-10-09 逾期，無法使用」；陳思妤的鋼琴補課選項與資料庫逐場比對，**只列出 6 個未滿的場次**，4/4 已滿、已停課、本身就讀的班級都被排除 | ✅ |
| 7 | 「出席」改「請假」後點數還原、補課資格產生；改回「出席」後重新扣點、資格作廢 | `Correcting_present_to_leave_restores_credit_and_grants_makeup_then_back_to_present_reverses_both` | 張家豪：出席 11 → 改請假 **12**，補課資格「可使用」→ 改回出席 **11**，資格「已作廢」；流水帳依序為扣點 −1、還原 +1、扣點 −1，皆在同一批（#14） | ✅ |
| 8 | 點數用完時無法點名為出席，畫面顯示原因 | `Cannot_mark_present_without_credits_and_reason_is_given`、`Expired_credits_cannot_be_used_and_reason_mentions_expiry` | 郭品妍（0 點）點出席 → 畫面顯示「**點數不足（剩餘 0 點），請先購買點數。**」，名單上也標示「點數不足，無法點名為出席」 | ✅ |
| 9 | 退費金額與手算一致（至少兩批不同單價＋一批已過期） | `Refund_matches_hand_calculation_with_two_prices_and_an_expired_batch`（= 11,750）、`Refund_rounds_half_away_from_zero_once_on_the_total`、`Refund_quote_matches_hand_calculation` | 許安妮：11 × 700 + 10 × 800 = **15,700**，已過期 1 × 900 不退；畫面試算 NT$ 15,700 | ✅ |
| 10 | 退費確認後剩餘點數為 0，之後無法點名為出席 | `Confirmed_refund_zeroes_credits_and_blocks_further_attendance` | 確認退費後：退費紀錄 15,700 元與 3 筆逐批明細；所有批次（含過期）合計 **0**；在當天場次點出席 → 「點數不足」 | ✅ |
| 11 | 提醒名單涵蓋「剩餘點數不足」與「即將到期」，並有單元測試 | `ReminderRulesTests` ×4、`Reminder_list_covers_low_balance_and_expiring_soon` | 畫面 6 位（3 位點數不足、3 位即將到期），與獨立 SQL 計算**完全一致**；已退費學生不列入 | ✅ |
| 12 | 月報點數收入與手算一致 | `Revenue_matches_hand_calculation_including_a_restored_deduction`（= 3,050）、`Monthly_report_matches_hand_calculation`（= 3,850 / 800） | 10 月四位老師的授課堂數、扣點數、收入與獨立 SQL 計算**完全一致**（合計 24 堂、44 點、NT$ 32,850） | ✅ |
| 13 | 老師看不到其他老師的課程，也無法進入退費與月報頁；直接輸入網址會被導回或顯示無權限 | `Teacher_only_sees_own_courses_and_sessions`、`Teacher_cannot_take_attendance_for_another_teachers_session`、`Teacher_cannot_confirm_refund`、`Teacher_cannot_correct_attendance`、`Teacher_cannot_buy_credits`、`Teacher_cannot_read_student_details` | 老師 `chen`：場次列表沒有其他老師的課；10 個店長頁面網址全部導向 **/forbidden**；開啟其他老師的場次顯示「您沒有權限存取其他老師的課程」，且**沒有洩漏學生名單**。未登入時導向登入頁 | ✅ |
| 14 | 單元測試至少 20 個，`dotnet test` 全數通過 | **73 個測試案例全部通過**（含兩輪審查後新增的 12 個），GitHub Actions 每次 push 自動執行 | — | ✅ |
| 15 | 交付物 | — | 見第 4 節 | ✅ |

## 3. 資料一致性檢查

範例資料（重演 10 週營運）加上端對端操作後，對整個資料庫執行以下檢查，**應為 0 的項目全部為 0**：

| # | 檢查項目 | 結果 |
|---|---|:-:|
| 1 | 一般出席／缺席紀錄中，淨扣點不是 −1 的筆數 | 0 |
| 2 | 請假紀錄中，淨扣點不是 0 的筆數 | 0 |
| 3 | 請假紀錄中，沒有有效補課資格的筆數 | 0 |
| 4 | 補課點名有扣點的筆數 | 0 |
| 5 | 補課出席，但資格不是「已補課」 | 0 |
| 6 | 補課缺席，但資格不是「作廢」 | 0 |
| 7 | 逾時請假卻記為請假（未經店長修正） | 0 |
| 8 | 請假「是否準時」與 24 小時規則不一致 | 0 |
| 9 | 補課資格有效期不是 60 天 | 0 |
| 10 | 補課預約的場次超過資格有效期 | 0 |
| 11 | 補課預約在一對一或不同科目 | 0 |
| 12 | 有效場次人數（固定學生＋補課）超過 4 人 | 0 |
| 13 | 從已過期的批次扣點 | 0 |
| 14 | 任一批次剩餘點數為負數 | 0 |
| 15 | 到期日不是購買日 + 6 個月 | 0 |
| 16 | 已退費學生剩餘點數不為 0 | 0 |

## 4. 交付物與技術限制

| 項目 | 狀態 |
|---|:-:|
| 方案檔 `LessonCredits.slnx`、原始碼、migration（`src/LessonCredits.Core/Data/Migrations`） | ✅ |
| README：啟動方式、範例帳號、扣抵規則、退費計算、收入認列、權限規則、選擇 Cookie 驗證的理由 | ✅ |
| 畫面截圖：點名、學生點數明細、退費試算、月報（另含提醒名單、老師視角、無權限、逾時請假） | ✅ |
| 範例資料：4 位老師、30 位學生、10 門課（≥ 6）、8 位學生持有多批點數、4 批已過期未用點數、25 筆補課資格（含可使用、已預約、已補課、補課缺席作廢、已逾期）、1 筆退費、2 個停課日 | ✅ |
| xUnit 測試專案 | ✅ |
| .NET 9 以上（使用 .NET 10）、Blazor Web App Interactive Server | ✅ |
| EF Core + SQLite，附 migration | ✅ |
| 時間相關邏輯一律透過 `TimeProvider` 注入 | ✅（見 D-0）|
| 不需 Docker，`dotnet run` 即可啟動 | ✅ |

## 5. 發現的問題與處理

| # | 問題 | 嚴重度 | 處理 | 驗證 |
|---|---|---|---|---|
| D-0 | 新增課程表單的預設時間用了 `DateTime.Today`，違反「時間一律透過 TimeProvider」 | 低（只是時刻預設值，不影響業務邏輯） | 改用 `TimeOnly`，表單直接綁定時刻，不再需要日期 | 全專案掃描已無 `DateTime.Now/Today/UtcNow` |
| D-1 | 課程列表沒有依週一到週日排序 | 低（顯示順序） | 星期在資料庫存成字串，SQL 排序變成字母序；改為載入後在記憶體排序。先補上失敗測試 `Courses_are_listed_from_monday_to_sunday_then_by_time` 再修正 | 單元測試通過；畫面依週一 → 週日顯示 |
| D-2 | 逾時請假的提示用綠色「成功」樣式，容易誤以為請假成功 | 低（介面） | 新增黃色「警告」樣式，訊息改為「……視為缺席（仍會扣點）」 | 畫面確認為黃色警告（截圖 `e2e-late-leave.png`） |

## 6. 測試限制

- 端對端測試使用真實時鐘，因此 24 小時規則在畫面上是以「提前 24h15m / 23h45m」驗證兩側行為；**精確到 1 分鐘的邊界**（24h01m、23h59m、剛好 24h）由使用 `FakeTimeProvider` 的單元測試驗證。
- 「請假不扣點並產生補課資格」需要先準時請假、再等到上課開始後才能點名，無法在一次端對端測試中以真實時間完成；改由單元測試，加上範例資料中 25 筆請假紀錄的一致性檢查（#2、#3）驗證。
- 沒有使用 bUnit 元件測試（題目列為選配）；畫面行為以 Playwright 端對端操作驗證。

## 7. 程式碼審查後的修正

外部審查以讀程式碼的方式提出以下問題。每一項都**先寫測試重現問題（確認失敗）**，再修正並確認通過。

| # | 問題 | 重現結果（修正前） | 修正 | 新增測試 |
|---|---|---|---|---|
| R-1 | 退費後修正點名，已退費的學生會重新擁有點數。另外發現：已退費的學生若有準時請假紀錄，仍可被點名為「請假」並拿到補課資格（「請假」不需要點數，只看餘額擋不住） | 3 個測試失敗：退費後把出席改請假，點數 0 → 1，並產生補課資格；退費學生仍可請假、可點名為請假 | 點名與請假檢查學生是否在學；退費確認時間晚於該場次時，拒絕會改變點數或補課資格的修正（出席與缺席互改仍允許） | `RefundGuardTests` ×4 |
| R-2 | 扣點、退費、補課預約都是「先讀再寫」，讀取不在交易內，同時操作可能扣成負數或超收 | 3 個測試失敗：剩 1 點時兩邊同時點名 → 餘額 **−1**；退費與點名同時進行 → 剩 9 點卻退 **8,000**（多退 800）；剩 1 個名額時兩筆預約同時送出 → 小班 **5 人** | 點名、修正、請假、退費、補課預約與取消、停課、選課，都在第一次讀取前開始交易（SQLite `BEGIN IMMEDIATE`，寫入依序排隊） | `ConcurrencyTests` ×3（暫存檔資料庫＋第二條連線先佔寫入鎖，確定性重現；連續執行 5 次皆通過） |
| R-3 | 沒有 CI 與 LICENSE | — | 新增 GitHub Actions（restore → 警告視為錯誤建置 → 測試 → 上傳測試結果）；新增 MIT 授權 | — |

### 第二輪審查

| # | 問題 | 重現結果（修正前） | 修正 | 新增測試 |
|---|---|---|---|---|
| R-4 | 事後補點名可以繞過退費保護：保護只在「修正點名」，沒有在「點名」 | 3 個測試失敗：10/20 未點名 → 10/25 退費 → 10/26 重新購買 → 10/27 補點 10/20，出席與缺席都從新買的批次扣點，請假則產生新的補課資格 | 點名也套用同一條規則：場次開始時間早於最近一次退費者不得補點名；退費後重新購買、之後才上的課照常點名 | `Session_before_refund_cannot_be_recorded_late_even_after_repurchase` ×3、`Session_after_repurchase_can_be_recorded_normally` |
| R-5 | 扣點未限制「購買日不得晚於場次日期」，README 也沒寫 | 現行行為，非錯誤 | **維持允許**：補點舊場次時，家長可能事後才買點數補足欠的課；若禁止，老師實際上了課卻永遠無法點名。退費情境已由 R-4 排除。README 扣點規則新增「補點名」一列說明 | `PickBatchForDeduction_can_use_a_batch_bought_after_the_session_date`（固定此行為） |
| R-6 | 併發測試以 300 ms 時間差假設受測操作已讀取，機器慢時錯誤實作可能誤判為通過 | — | 改用 EF Core interceptor：受測操作**開始交易**時發出訊號，另一條連線才 commit。正確實作在讀取前開始交易；錯誤實作要到 SaveChanges 才開始交易，此時已讀到舊資料；沒有交易則逾時失敗 | 突變測試：把讀取移回交易外，3 個測試連續 3 次**全部失敗**；正式程式碼連續 5 次全部通過 |

R-6 的過程中，第一版訊號設在「第一次讀取」，突變測試發現錯誤實作仍會通過：操作的第一次讀取是載入場次，關鍵的餘額讀取在後面，另一方因此太早 commit。改為「開始交易」後才能正確區分，這也是為什麼要用突變測試驗證測試本身。

## 附錄：單元測試清單（73 個案例，全部通過）

**規則層（Rules）**

- CreditRulesTests：`ExpiryDate_is_six_months_after_purchase`、`ExpiryDate_clamps_to_end_of_shorter_month`、`Batch_is_usable_on_expiry_date_but_not_the_day_after`、`UsableBalance_excludes_expired_batches`、`PickBatchForDeduction_takes_the_earliest_expiring_batch_first`、`PickBatchForDeduction_skips_expired_and_empty_batches`、`PickBatchForDeduction_returns_null_when_no_usable_credit`、`PickBatchForDeduction_can_use_a_batch_bought_after_the_session_date`
- LeaveAndMakeupPolicyTests：`Leave_requested_24h01m_before_class_is_on_time`、`Leave_requested_23h59m_before_class_is_late`、`Leave_requested_exactly_24h_before_class_is_on_time`、`Makeup_credit_is_valid_on_day_60_but_not_day_61`、`Deducts_credit_only_for_regular_present_or_absent`（5 個案例）、`Only_regular_leave_grants_makeup_credit`
- MoneyRulesTests：`Refund_matches_hand_calculation_with_two_prices_and_an_expired_batch`、`Refund_rounds_half_away_from_zero_once_on_the_total`、`Money_round_uses_half_away_from_zero_not_bankers_rounding`、`Revenue_matches_hand_calculation_including_a_restored_deduction`
- ReminderRulesTests：`Flags_low_balance_at_two_credits_or_fewer`、`Flags_credits_expiring_within_30_days_inclusive`、`Used_up_batches_do_not_count_as_expiring`、`Can_match_both_conditions_at_once`

**服務層（Services，SQLite in-memory）**

- CreditAndAttendanceTests：`Buying_ten_lesson_plan_adds_ten_credits_expiring_in_six_months`、`Teacher_cannot_buy_credits`、`Present_or_absent_deducts_one_credit`（2 個案例）、`On_time_leave_does_not_deduct_and_grants_makeup_credit`、`Late_leave_cannot_be_recorded_as_leave_and_absence_deducts`、`Cannot_mark_present_without_credits_and_reason_is_given`、`Expired_credits_cannot_be_used_and_reason_mentions_expiry`、`Deducts_from_earliest_expiring_batch_first`、`Teacher_cannot_take_attendance_for_another_teachers_session`、`Cannot_take_attendance_before_class_starts`、`Correcting_present_to_leave_restores_credit_and_grants_makeup_then_back_to_present_reverses_both`、`Correcting_present_to_absent_does_not_touch_credits`、`Teacher_cannot_correct_attendance`
- ScheduleAndMakeupTests：`Generates_eight_weeks_of_sessions_and_is_idempotent`、`Closure_day_cancels_all_sessions_that_day_and_no_credits_are_deducted`、`Closure_day_is_skipped_when_generating_sessions`、`Makeup_credit_older_than_60_days_cannot_be_used`、`Expired_makeup_credit_cannot_be_booked_even_for_an_earlier_session`、`Cannot_book_makeup_into_a_full_group_class`、`Makeup_bookings_count_towards_capacity`、`Cannot_book_makeup_into_other_subject_or_one_on_one`、`Makeup_present_uses_credit_without_deduction`、`Makeup_absent_forfeits_credit_without_deduction`、`Leave_from_one_on_one_grants_credit_bookable_only_in_group_class`、`Source_attendance_cannot_be_corrected_back_once_makeup_credit_is_used`
- RefundAndReportTests：`Refund_quote_matches_hand_calculation`、`Confirmed_refund_zeroes_credits_and_blocks_further_attendance`、`Teacher_cannot_confirm_refund`、`Reminder_list_covers_low_balance_and_expiring_soon`、`Monthly_report_matches_hand_calculation`
- RefundGuardTests：`Correction_that_changes_credits_is_rejected_after_refund`、`Correction_that_does_not_change_credits_is_still_allowed_after_refund`、`Refunded_student_cannot_be_recorded_even_as_leave`、`Refunded_student_cannot_request_leave`、`Session_before_refund_cannot_be_recorded_late_even_after_repurchase`（3 個案例）、`Session_after_repurchase_can_be_recorded_normally`
- ConcurrencyTests（暫存檔資料庫＋開始交易訊號）：`Concurrent_deductions_cannot_overdraw_the_last_credit`、`Refund_racing_with_a_deduction_settles_the_latest_balance`、`Concurrent_makeup_bookings_cannot_exceed_group_capacity`
- AdminServiceTests：`Changing_plan_price_does_not_affect_purchased_batches`、`Cannot_enroll_more_than_capacity`、`Teacher_cannot_read_student_details`、`Teacher_only_sees_own_courses_and_sessions`、`Courses_are_listed_from_monday_to_sunday_then_by_time`
