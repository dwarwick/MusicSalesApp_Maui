# Android Listing Requirements — Google Play Store

Action items identified from a comprehensive review of all Google Play Developer Program Policies (April 2026).

## HIGH Priority (Required Before App Approval)

### 1. Account Deletion
Google Play requires that if your app allows account creation, it must also allow users to request account deletion — both **in-app** and via a **web URL** entered in Play Console.

- [X] Add an account deletion option accessible from the app (e.g., Settings / Manage Account)
- [X] Provide a web URL for account deletion (to enter in Play Console) — use `https://streamtunes.net/manage-account`
- [X] Ensure deletion removes associated user data (or clearly disclose retention practices)

### 2. In-App Privacy Policy Link
A privacy policy link must be accessible **within the app itself**, not just in the Play Store listing.

- [X] Add a link to `https://streamtunes.net/privacy-policy` inside the app (e.g., Settings or About page)

### 3. Data Safety Section
Complete the Data Safety section in Play Console. Use the Google Play data type categories below.

**Personal info:**
- [ ] Email address (account creation, login, email changes)
- [ ] User IDs (server-assigned user ID, stored locally and sent with API requests)

**Financial info:**
- [ ] Purchase history (Google Play subscription purchase tokens and order IDs sent to server for verification)

**App activity:**
- [ ] App interactions (stream counts recorded per song after qualifying listen duration)
- [ ] Other actions (like/dislike on songs, song reports with reason text)

**Data shared with third parties:**
- [ ] Google (purchase tokens/order IDs via Google Play Billing SDK)
- [ ] Facebook (public song URL only, user-initiated sharing via Android intent — no SDK)

**Data NOT collected:** location, name, phone number, address, photos, videos, audio files, contacts, calendar, device IDs, crash logs, diagnostics, health/fitness, messages, web browsing, playlists (not implemented in app)

### 4. Privacy Policy URL in Play Console (Play Console only — not a code change)
- [ X ] Enter the privacy policy URL (`https://streamtunes.net/privacy-policy`) in the designated Play Console field

## MEDIUM Priority

### 5. In-App Content Reporting (UGC Policy)
Since creators upload music that users listen to, this qualifies as User Generated Content. The UGC policy requires an in-app mechanism for users to report objectionable content.

- [X] Add a "Report" option for songs (e.g., long-press menu or overflow button on song items)
- [X] Route reports to an admin review process on the server

### 6. Verify Subscription Screen Disclosures
The Subscriptions policy requires clear disclosure of all terms before enrollment.

- [X] Subscription price is clearly shown
- [X] Billing frequency (monthly) is stated
- [X] Auto-renewal terms are disclosed
- [X] Cancellation instructions are provided (link to Google Play Subscription Center)

## LOW Priority

### 7. DMCA / Copyright Response Process
The Intellectual Property policy requires responding to copyright takedown notices. The web server already has a Creator Agreement with rights ownership warranty (Section 3).

- [X] Verify DMCA takedown process is documented and operational — report song feature includes "Copyright Violation" reason
- [X] Consider adding a copyright reporting link accessible from the app
