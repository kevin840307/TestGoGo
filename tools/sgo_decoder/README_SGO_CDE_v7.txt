SGO / Stargate Online SPR CDE → PNG 增量解碼工具 v7
===============================================

這一版專門修正 v6 的下列錯誤：
  CDE decrypt failed: Unexpected 17/18/19 bytes after raw DEFLATE end;
  at most 15 bytes expected for AES block alignment

【真正原因與修正】
你提供的真實加密檔 EN050C_ND1.spr（16,626 bytes）已實際成功解析：
  AES-256-CBC -> Raw DEFLATE -> 一般 SPR（36,962 bytes）-> 5 個影格 PNG。
Raw DEFLATE 結束後的尾端實際包含：
  00 00 FF FF 00  +  00 * 12  （總共 17 bytes）
v6 只接受至多 15 bytes，誤將合法資料視為損壞。
v7 兼容舊版「<=15 bytes」尾端，並額外允許觀察到的
  00 00 FF FF 00 + 0~15 bytes 的零填充（最多 20 bytes）。
仍然驗證資料長度、AES 資料對齊、Raw DEFLATE 結束標記、SPR 像素／影格。
不是任意忽略所有多出來的資料；超過此範圍／內容異常仍會 FAIL。

【安裝】
Windows CMD / PowerShell：
  py -m pip install Pillow cryptography

【只補跑 v6 失敗 CDE；原本成功 PNG 不覆寫】
請先將此 ZIP 內全部檔案解壓至 C:\SGO\Stargate Online IE HK\
在該資料夾以 CMD 一行執行：

py sgo_spr_retry_type_modes_v7.py "C:\SGO\Stargate Online IE HK\output" -o "C:\SGO\Stargate Online IE HK\Data1_PNG2" --recursive --metadata --spr-type cde --failed-log "C:\SGO\Stargate Online IE HK\Data1_PNG2\spr_retry_report.csv" --report "C:\SGO\Stargate Online IE HK\Data1_PNG2\spr_retry_report_v7.csv"

--failed-log 是 v6 舊 FAIL CSV，只有它標為 FAIL/CDE_DECRYPT_FAILED/UNSUPPORTED_CDE 的檔案才重跑。
--report 指向 v7 新報告，避免覆蓋 v6 舊報告。
輸出 PNG 保留在 Data1_PNG2，沒有 --overwrite 就不會覆寫已成功 PNG。

【不用舊失敗報告也能直接補跑】
py sgo_spr_retry_type_modes_v7.py "C:\SGO\Stargate Online IE HK\output" -o "C:\SGO\Stargate Online IE HK\Data1_PNG2" --recursive --metadata --spr-type cde --report "C:\SGO\Stargate Online IE HK\Data1_PNG2\spr_retry_report_v7.csv"

【單檔測試】
py sgo_spr_retry_type_modes_v7.py "C:\SGO\Stargate Online IE HK\output\Enemy\EN050C_ND1.spr" -o "C:\SGO\Stargate Online IE HK\Data1_PNG2\Enemy" --spr-type cde --metadata

【執行測試】
py -m unittest -v test_sgo_cde_v7
本包附兩個真實 CDE 加密樣本供測試：
- Normal_Effect24.spr + ACT（20 影格；v6 已成功）
- EN050C_ND1.spr（5 影格；v6 因尾端 17 bytes 誤判）
13 項測試已 PASS。包含真實圖片解析、資料保護、續傳、FAIL CSV、
16~20 byte 合法尾端以及不合法尾端拒絕。

【限制】
這版只用你提供的兩個真實加密 SPR 做完整驗證。
日誌中其餘 18/19 bytes 的檔案很可能使用相同填充形式，但尚未逐一驗證。
對任何其他失敗檔請保留新 CSV 中的原因，不要視為已全部修復。
