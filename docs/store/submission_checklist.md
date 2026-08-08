# Submission Checklist — Runewake: The Buried Age

This checklist covers every step required before submitting to the Apple App Store and Google Play Store. Each item is a manual action. The agent cannot perform these steps (no signing credentials, no store console access).

---

## 1. Documentation Placeholders

- [ ] Replace all **[DATE — UPDATE BEFORE SUBMISSION]** markers in `docs/store/privacy_policy.md` with the actual effective date.
- [ ] Replace **[privacy@runewake.com — UPDATE BEFORE SUBMISSION]** with the real contact email address throughout `docs/store/privacy_policy.md`.
- [ ] Replace **[JURISDICTION — UPDATE BEFORE SUBMISSION]** in the privacy policy with your governing law (e.g., "Ontario, Canada" or "Delaware, United States").
- [ ] Replace **[ADD ADDRESS BEFORE SUBMISSION]** in the privacy policy with your physical mailing address, or remove the line if not required by your jurisdiction.
- [ ] Replace **https://runewake.com/support [placeholder]** in all store listings with the real support URL.
- [ ] Replace **https://runewake.com/privacy [placeholder]** in all store listings with the real privacy policy URL.
- [ ] Verify the **In-App Purchase name** and **description** in both store listing files match exactly what you enter in App Store Connect / Google Play Console.

---

## 2. URLs and Hosting

- [ ] Upload the privacy policy to a public URL (e.g., `https://runewake.com/privacy`). This can be a static page served from GitHub Pages, Netlify, or any hosting provider.
- [ ] Create a `/support` page or email redirect (e.g., `https://runewake.com/support` → mailto: support@runewake.com).
- [ ] Register the domain `runewake.com` (or the domain you intend to use) and configure DNS.
- [ ] Ensure both URLs return HTTPS (not HTTP). Both stores reject non-HTTPS URLs.
- [ ] Test the privacy policy URL in a WebView to confirm it renders correctly on mobile.

---

## 3. Code Signing (Android)

- [ ] Generate a release keystore (use `keytool` or Android Studio):
      ```
      keytool -genkey -v -keystore exports/release.keystore \
        -alias runewake -keyalg RSA -keysize 2048 -validity 10000
      ```
- [ ] Place the keystore at `client/exports/release.keystore`.
- [ ] Fill in `keystore/release_password` in `client/export_presets.cfg` (currently empty).
- [ ] Keep the keystore password and alias in a secure password manager. **Losing the keystore = losing the ability to publish updates to the same app.**
- [ ] Build the release APK:
      ```
      godot --headless --export-release "Android Release" exports/Runewake-release.apk
      ```
- [ ] Verify the APK is signed:
      ```
      jarsigner -verify -verbose -certs exports/Runewake-release.apk | grep "CN="
      ```
- [ ] (Optional) Run the APK on a physical device before uploading.

---

## 4. Code Signing (iOS)

- [ ] Enrol in the Apple Developer Program ($99/year) if not already enrolled.
- [ ] Create a Distribution certificate in the Apple Developer Portal.
- [ ] Create a Provisioning Profile for App Store distribution.
- [ ] Build the iOS archive in Xcode or via Godot:
      ```
      godot --headless --export-release "iOS" exports/Runewake.ipa
      ```
- [ ] Verify the IPA is signed with the Distribution certificate (not a Development certificate).
- [ ] Test the IPA on a physical device via TestFlight before submitting.

---

## 5. Screenshots — App Store (6 required)

- [ ] Capture **6 screenshots** on an **iPhone 6.7"** (iPhone 14 Pro Max or newer): 1290×2796 pixels.
- [ ] Capture **6 screenshots** on an **iPad 12.9"** (iPad Pro 12.9" gen 5 or newer): 2048×2732 pixels.
- [ ] Follow the screenshot descriptions in `docs/store/app_store_listing.md` §Screenshot Slot Descriptions.
- [ ] For each screenshot, ensure the game is in a visually interesting state (not empty/loading screens).
- [ ] Upload screenshots to App Store Connect for each device size.

---

## 6. Screenshots — Play Store (8 required)

- [ ] Capture **8 screenshots** on a **phone** (1080×1920 or larger): 2:1 aspect ratio supported.
- [ ] Capture **8 screenshots** on a **7" tablet** (1920×1200 or larger): landscape preferred.
- [ ] Follow the screenshot descriptions in `docs/store/play_store_listing.md` §Screenshot Slot Descriptions.
- [ ] Upload screenshots to Google Play Console for each device type.

---

## 7. App Icon and Graphics

- [ ] Create a **512×512 px app icon** (PNG, no alpha channel for App Store). Include rounded corners version and no-corners version.
- [ ] Create a **1024×500 px Play Store feature graphic** (JPG or PNG).
- [ ] (Optional) Create a **1024×1024 px App Store icon** (required for App Store Connect).
- [ ] Upload the icon and feature graphic to both consoles.

---

## 8. App Store Connect Setup

- [ ] Create a new app entry in App Store Connect.
- [ ] Set the **Bundle ID** to `com.runewake.buriedage` (must match the Godot export preset).
- [ ] Set **Pricing** to Free (with IAP).
- [ ] Configure the **In-App Purchase**: "Regions 2 & 3 — Full Campaign" as a one-time consumable or non-consumable purchase.
- [ ] Set **Availability** to all territories unless region-locked.
- [ ] Fill in the listing text from `docs/store/app_store_listing.md`.
- [ ] Upload the privacy policy URL.
- [ ] Upload the support URL.
- [ ] Set the **Age Rating** using the answers in `docs/store/age_rating_questionnaire.md`.
- [ ] Upload the build (IPA) via Xcode or Transporter.

---

## 9. Google Play Console Setup

- [ ] Create a new app entry in Google Play Console.
- [ ] Set the **App ID** to `com.runewake.buriedage` (must match the Godot export preset).
- [ ] Set **Pricing** to Free (with IAP).
- [ ] Configure the **In-App Purchase**: "Regions 2 & 3 — Full Campaign" as a managed product.
- [ ] Set **Countries** — select all countries unless region-locked.
- [ ] Fill in the listing text from `docs/store/play_store_listing.md`.
- [ ] Upload the privacy policy URL.
- [ ] Complete the **Content Rating** questionnaire using the answers in `docs/store/age_rating_questionnaire.md`.
- [ ] Upload the APK to the "Production" track.
- [ ] Complete the **App Content** section (target audience, ads, etc.).

---

## 10. Pre-Submission Checks

- [ ] Run `dotnet test` on the latest commit — all 443+ tests must pass.
- [ ] Build the release APK (`godot --headless --export-release "Android Release"`).
- [ ] Build the release IPA (via Xcode or Godot iOS export).
- [ ] Verify the app launches and the full game loop (title → map → duel → win → map) works on a physical device.
- [ ] Verify the IAP screen shows correctly (even if not yet connected to the store sandbox).
- [ ] Verify the privacy policy link is accessible from the app.
- [ ] Verify the app icon displays correctly on the home screen.
- [ ] Test offline mode: enable Airplane Mode, launch the app, confirm it works without any network calls.

---

## 11. Submission

- [ ] Submit to **App Store Review** via App Store Connect ("Submit for Review").
- [ ] Submit to **Google Play Review** via Play Console ("Send for Review").
- [ ] App Store review typically takes **1–3 days**.
- [ ] Google Play first review typically takes **1–7 days**.

---

## 12. Rejection Handling

- [ ] If rejected, read the rejection reason carefully. Do not resubmit without understanding the issue.
- [ ] Common rejection causes:
      - **Metadata issue**: Fix the description, keywords, or age rating.
      - **Crash on review device**: Test on a real device (not simulator). Fix the crash in the engine.
      - **Incomplete IAP**: Set up the IAP product in the console before submitting the build.
      - **Privacy policy URL not loading**: Verify the URL is HTTPS and renders correctly.
      - **Login required**: Ensure the app does not require account creation to play (v1 does not).
- [ ] After fixing, increment the build number and re-upload.
- [ ] Log the rejection and fix in `docs/AGENT_LOG.md` so the pattern is not repeated.

---

## 13. Post-Launch

- [ ] Monitor crash reports (Godot crash logs are at `user://data/crash_log.txt` on device).
- [ ] Monitor Supabase telemetry for game balance issues.
- [ ] Respond to user reviews in the first week.
- [ ] Plan the next update cycle per the roadmap.

---

**End of checklist.**