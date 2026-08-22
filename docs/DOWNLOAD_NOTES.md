# Download Notes — read before troubleshooting APK delivery

CONFIRMED 2026-08-21: Trikzos's stuck-at-100%-forever download failures
were caused by Telegram's built-in in-app browser (the mini viewer that
opens when a link is tapped inside Telegram chat — distinct address bar
with an X/chevron instead of tabs, a translate icon, no real Chrome UI).
That browser cannot reliably finalize large (~190MB) file downloads.

FIX THAT WORKED: copy the raw APK link out of Telegram and paste it
directly into a real browser (Chrome), rather than tapping the link inline.
Confirmed working by Trikzos.

STANDING RULE: whenever posting an APK link to the Runewake Telegram
group, include this instruction explicitly in the message: "Copy this
link and open it in Chrome — don't tap it directly, Telegram's built-in
browser can't finish large downloads." Do this every time, don't rely on
Trikzos remembering.