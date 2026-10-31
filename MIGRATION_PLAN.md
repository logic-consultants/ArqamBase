# ArqamBase: Access → WinForms Migration Plan

Source: `ArqamBaseV25.9.accdb` (export in `access-export/`) · Database: `ArqamBase_be` on DAS-SVR

## 1. Technology

| Part | Choice |
|---|---|
| App | WinForms, .NET 10 (LTS) |
| Data access | EF Core (scaffolded from `ArqamBase_be`) for screens; plain SQL / views for reports |
| Sign-in | Windows authentication — no login screen; Windows user mapped to app role |
| Reports / printing | QuestPDF (preview + print + PDF) — free under its Community licence (check eligibility) |
| Excel export | ClosedXML |
| Look and feel | Access style: Krypton Toolkit ribbon, collapsible navigation pane, tabbed screens, grids with column sort/filter dropdowns. Themes: Light, Dark, or follow Windows (saved per user) |
| Settings / API keys | `appsettings.json` + Windows-protected secrets, never in code |

Access and the new app run side by side on the same database; users switch one area at a time.

## 2. Before starting (Phase 0)

1. **Remove `sa` from the front end.** All 57 linked tables use DSN `ArqamBase` with the `sa` password. Switch to Windows auth (`Trusted_Connection=Yes`) and change the `sa` password.
2. **Rotate WhatsApp API keys.** GreenAPI and waapi.app tokens are hard-coded in the VBA.
3. **Remove broken links:** `tblAttendance`, `tblExamResultsDetails`, `tblExamResultsMaster`, `tblIncrementDetails`, `tblIncrementHead`, `tblResultTypes` (not in the database).
4. ~~Answer the open questions~~ Done (section 7).

## 3. Screens to build

| New screen | Replaces (Access) |
|---|---|
| Main menu | `frmMain`, ribbon `UmmahCollege` |
| Students | `frmStudents`, `frmConversations` |
| Family | `frmFamilyDetails`, `frmFamilyDetails_Sub`, `FamliyFlag` |
| Student Fee Ledger (receive fee, add fee, auto insert) | `Ledger`, `ledger_sub`, `Ledger_sub_Paid`, `frmFeeDef` |
| New Fee | `frmNewFee`, `New Fee`, `FrmFeeInput`, `FeeInput` |
| Periodic Fee Update | `Feeupdate` |
| Fee Definition | `frmFeeDefinition`, `Fee Definition` |
| Class Fee Increase | `frmClassFeeIncrease` |
| Class Information & Promotion | `frmClassInformation`, `frmClassPromote` |
| Print Challans | `frmPrintChallanClasewise` |
| Reports centre | `Classselect`, `Report Date Range`, `frmDateRange_Daily` |
| Staff | `frmStaff` |
| Attendance: mark by class/section and date (P/A/L/T), holidays, monthly summary, absence WhatsApp alerts | `tblAttendanceMaster`, `frmAttandance*`, `frmClassAttendance`. New tables: `sql/attendance_install.sql` |
| WhatsApp: unpaid sender, send message, log | `frmUnpaidWhatsApp`, `frmWhatsAppsend`, `frmWhatsApp_Com`, `WAMessagesLog` |
| SMS settings | `SMSSettings`, `smsGrid`, `*_Family_SMS` |
| Google Contacts sync | `GoogleContacts`, `modGoogleContacts` |
| Setup lists: Classes, Sections, Departments, Conversation types, Campus, Users | `ClassDefinitions`, `Sections`, `frmConvoTypes`, `CampusInfo`, `UsersMain`, `UserDetails` |

Not rebuilt: `frmLogin` (Windows sign-in instead), `frmBackup` (use a SQL Server backup job), the ribbon modules.

## 4. Reports: 62 → 15

| New report (with filters) | Replaces |
|---|---|
| Fee Challan (student / family / class / section / period / flagged) | `RPT_FeeReceipt`, `_1`, `_Date`, `_Students`, `rpt_FeeReceipt_Single` |
| Unpaid List (by student / family / class / department; top defaulters; Excel/PDF) | `Unpaid List`, `Unpaid Detail`, `Unpaid_Detail_family`, `Unpaid_Detail_ClassPDF`, `Unpaid SMSl_family`, `rpt_TopDefaulters` |
| Defaulter Letter (reminder / notice / struck-off) | `LetterUnpaid`, `LetterUnpaid1`, `UnpaidLetter`, `rpt_TopDefaulters_Notice`, `StruckOff Notice`, `StruckoffNotice` |
| Daily Fee Received (by date / deposit / receipt no.) | `Daily Fee Received Report`, `…Report1`, `rptDailyFeeReceived`, `rptDailyFeeReceivedReport`, `rptDailyFeeRR`, `rptDailyFeeRR_DepositWise`, `rpt_DailyFeeReceipt_Receiptwise` |
| Fee Breakup (all / admission / periodic) | `FeeBreakup`, `FeeBreakup ADM Fee`, `FeeBreakup Periodic` |
| Class-wise Students & Fees (detail / summary / strength) | `rptClassWiseStudentsDetails`, `rptClassWiseStudentslist`, `studentsFeeClasswise`, `SummeryClassWiseFeeDetail`, `RptClassWiseDetails`, `rptFeeStrengthSummary` |
| Student List (choose columns: address, phones, polio, weekly test…) | `Students`, `Students List`, `Students Detail Address and Ph`, `Studnet's Phone Numbers`, `PH_SMS`, `QryCCwiseReport`, `rpt_Students_reconcile`, `rptPolioList`, `WeeklyTestReport`, `With out Admission Report` |
| Admissions & Withdrawals (register / by month / dropouts) | `ADM_WD Register`, `Admission Report By Month`, `Withdrawal Report By Month`, `rptAdmisstionRerot`, `rptDropOutReprot` |
| Labels (students / workbooks) | `Labels Students`, `WorkBooks Labels` |
| Delivery & Task Lists | `FeeDeliveryProforma`, `rptStudentsListDelivery`, `rptPaidChallnforSummarvacation`, `rptPendingSummarTaskList` |
| Fee Increase Log | `rptFeeIncreasedLog` |
| Staff Personal | `TeachersPersonal` |
| School Leaving Certificate | `School Leaving Certificate` |
| Bonafide Certificate | (from `frmStudents`) |
| ID Card | `IDCard` |

Accounting reports (`Transaction Listing`, `Voucher_Journal`, `Voucher_Typewise` and the 25 accounting queries) are redesigned in phase 8, not converted.

## 5. Build order

| Phase | Scope | Users can switch when done |
|---|---|---|
| 1 Foundation | Solution, DB access, Windows-user roles, main menu, setup lists | — |
| 2 Students & Family | Students, Family, certificates, ID card | Admissions office |
| 3 Fees | Ledger, New Fee, Fee Update, Fee Definition, Fee Increase, Challans | Fee counter |
| 4 Reports | Reports centre + the 15 reports, Excel export | Management |
| 5 Messaging | WhatsApp, SMS, Google Contacts | — |
| 6 Staff & Attendance | Staff, Attendance, Class promotion | Everyone; retire Access |
| 7 Salaries | Salary heads, monthly salaries, increments (tables exist but are empty; increment tables to be created) | Accounts office |
| 8 Accounting | New design: account types, accounts, vouchers, ledgers, cash book, receipts & payments, P&L, balance sheet. Fee receipts post automatically | Accounts office |

## 6. Not migrating

- **Share Holder Record**: `Investors` tables don't exist.
- **Empty tables** (0 rows): Admission Tests, Meetings, Principal Remarks, Hifz Completed, Sabqi, Syllabus, Staff Requirement, Periods, Round Report.

## 7. Decisions

| Question | Decision |
|---|---|
| Extra modules | Keep **Accounting** and **Salaries** (phases 7–8). Accounting is new: its tables don't exist yet |
| WhatsApp provider | **Both** GreenAPI and waapi.app; chosen in settings |
| Roles | **Admin** (everything), **Fee clerk** (ledger, receive fee, challans), **Admissions** (students, families), **Read-only** (view and print reports) |
| Challan paper | **Plain A4**; the app prints the full layout |
