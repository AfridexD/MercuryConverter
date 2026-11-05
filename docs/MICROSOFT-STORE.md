# Publishing Mercury Converter on the Microsoft Store

This is a step-by-step guide. The GitHub download is **not** the file the Store needs: the Store wants an `.msix` package, and the Store signs it for you, so no code-signing certificate is needed. A Store install also does not show the Windows "unknown publisher" warning.

## What is ready, and what you do

| Step | Who |
|---|---|
| 1. Make a Microsoft Partner Center developer account | You |
| 2. Reserve the name "Mercury Converter" | You |
| 3. Copy three "Product identity" values | You |
| 4. Build the package with `scripts\build-msix.ps1` | Claude or you |
| 5. Test the package on your PC | You |
| 6. Fill in the submission in Partner Center and upload the `.msix` | You |
| 7. Wait for review, then it is live | Microsoft |

## 1. Make a Partner Center account

1. Go to <https://partner.microsoft.com/dashboard/registration> and sign in with a Microsoft account.
2. Choose **Individual** (not Company). Microsoft announced in September 2025 that individual registration is free. Older pages show a one-time $19 fee, so if you see a fee, check the price on the page before you pay.
3. Fill in your name, address and contact details, then verify your identity (a government ID and a selfie). This can take from minutes to a few days.

## 2. Reserve the name

1. In Partner Center open **Apps and games**, then **New product**, then **MSIX or PWA app**.
2. Type **Mercury Converter** and choose **Reserve product name**. If it is taken, add a few words, for example "Mercury Converter: Photo, Video and Audio". The name in the Store does not have to match the name shown inside the app.
3. A reserved name is yours for 3 months. Submit before then.

## 3. Copy the Product identity

Open your product, then **Product management**, then **Product identity**. Copy these three values exactly:

- `Package/Identity/Name`, which looks like `12345YourName.MercuryConverter`
- `Package/Identity/Publisher`, which looks like `CN=01234567-89AB-CDEF-0123-456789ABCDEF`
- `Package/Properties/PublisherDisplayName`, the name shown to people

Send them to Claude, or use them in step 4.

## 4. Build the package

On the PC that has the project and the `tools` folder:

```powershell
.\scripts\build-msix.ps1 -ForStore `
  -IdentityName "12345YourName.MercuryConverter" `
  -Publisher "CN=01234567-89AB-CDEF-0123-456789ABCDEF" `
  -PublisherDisplayName "Your Name" `
  -PackageDisplayName "The exact name you reserved"
```

This makes `dist\MercuryConverter-<version>-x64.msix` (about 165 MB). Do not sign it. The Store signs it.

Without `-ForStore` the script builds a package with test names. That one only proves that packing works. It cannot be uploaded.

The version comes from the project (for example 1.0.8 becomes package version 1.0.8.0). The Store requires the last number to be 0, and every new submission needs a higher version.

## 5. Test it on your PC first (recommended)

A Store reviewer opens the app. If it fails to start there, the submission is rejected, so check it yourself.

1. Turn on **Developer Mode**: Windows Settings, then System, then For developers (or Privacy and security, then For developers), then Developer Mode.
2. Close the normal Mercury Converter if it is open. Remove the GitHub version if you want a clean test.
3. In PowerShell, from the project folder:

   ```powershell
   Add-AppxPackage -Register .\publish\msix\AppxManifest.xml
   ```

4. Start **Mercury Converter** from the Start menu and check:
   - the window opens and shows the new owl icon
   - add a few photos, a video and an audio file, and convert each
   - right-click a photo, choose **Show more options**, then **Open with**, and pick **Mercury Converter**. Mercury should open with the photo already in its list
   - Settings: the "right-click menu" and "new version" switches should be hidden (the Store version has no menu of its own and is updated by the Store)
   - careful: this test build reads the same settings as your normal install, so it saves converted files to your usual Save to folder
   - close the app with files still in the list, open it again, and check the list came back
5. Remove it: `Get-AppxPackage *MercuryConverter* | Remove-AppxPackage`

If any of this fails, tell Claude what you saw. Please do not submit until it passes.

## 6. Fill in the submission

In Partner Center, open your product and start a **new submission**. These are the parts, in order.

**Pricing and availability**
- Price: **Free**. Markets: all you want. Visibility: public.

**Properties**
- Category: **Photo and video** (or **Utilities and tools**).
- Privacy policy URL: `https://afridexd.github.io/MercuryConverter/privacy.html`
- Website: `https://afridexd.github.io/MercuryConverter/`
- Support contact: `https://github.com/AfridexD/MercuryConverter/issues`
- System requirements: Windows 10 version 2004 or later, 64-bit. Keyboard and mouse.

**Age ratings**
- Answer the questionnaire honestly. Mercury has no violence, no chat, no purchases and no online features, so it should come out as everyone (3+).

**Packages**
- Upload the `.msix` from step 4. Partner Center checks it and shows the identity, the version, and the capability **runFullTrust**.

**Store listings** (English)
- Use the text below, add at least 1 screenshot (1366 x 768 or larger; the screenshots in `docs\images` are fine, or take new ones), and the logo (1:1, 300 x 300; use `branding\icon\png-blue\app-icon-256.png` enlarged from the 1024 file).

**Submission options, then Notes for certification**
- Paste the certification notes below.

Then **Submit to the Store**.

### Text for the listing

**Short description (up to 100 characters)**
> Convert photos, GIFs, video and audio on your PC. Free, private, nothing is uploaded.

**Description**
> Mercury Converter changes the type of your files. Turn a phone video into something small enough to send, a big photo into a light web image, or a song into an MP3.
>
> Drag in your files, pick a format, press Convert. That is all.
>
> • Free, with no ads and no account
> • Private: everything happens on your own computer and nothing is uploaded
> • Photos, GIFs, video and audio, including WebP, JPEG, PNG, AVIF, HEIC, MP4, MKV, GIF and MP3
> • Built for big jobs: tested with 26,000 photos in one go, with Pause and Resume
> • Never changes your original files and never makes a file bigger than the original
> • Right-click a file in File Explorer and choose Open with, Mercury Converter
> • Light and dark mode
>
> Mercury Converter is open source (MIT license): https://github.com/AfridexD/MercuryConverter
>
> It uses FFmpeg (GPL v3), ImageMagick and libjxl to do the conversions. Source code and licenses for these are listed at https://github.com/AfridexD/MercuryConverter/blob/main/THIRD-PARTY-NOTICES.md

**Keywords:** converter, photo, video, audio, webp, gif, mp4, mp3, jpeg, png, batch

**Notes for certification** (paste into Submission options)
> Mercury Converter is a Windows desktop app (Win32 packaged with MSIX). It requires the restricted capability runFullTrust because it starts the converter programs it carries in its own install folder (FFmpeg, ImageMagick, libjxl) as separate processes, and it writes converted files next to the user's original files or in a folder the user chooses. It makes no network requests.
>
> To test: open the app, drag in any photo, video or audio file, press Convert. The new file is saved next to the original. Right-click a photo or video in File Explorer and choose Open with, then Mercury Converter: the app opens with the file in its list, ready to convert.
>
> FFmpeg is GPL v3. Its source and the licenses are linked in the store description and in THIRD-PARTY-NOTICES.md.

## 7. After you submit

- Review usually takes from a few hours to a few days. You get an email either way. If it is rejected, the email says why. Send it to Claude and we will fix it.
- When it passes, the page goes live within a day. The Store link looks like `https://apps.microsoft.com/detail/<product id>`. Add it to the README and the website, and put the "Get it from Microsoft" badge on the download page.

## 8. Updating later

1. Change the version in the project, as for a GitHub release (for example 1.0.9).
2. Run the build command from step 4 again.
3. In Partner Center, start a new submission on the same product, upload the new `.msix`, and submit. Everyone who installed from the Store is updated automatically.

## What is different in the Store version

| | GitHub installer | Store version |
|---|---|---|
| Right-click menu | A Mercury Converter submenu with a "To WebP", "To MP4" and so on line per format | **Open with**, then Mercury Converter (adds the file to the list). A Store app cannot add its own submenu to File Explorer |
| Updates | Optional "new version" notice | The Store updates it. The app never contacts GitHub |
| "Unknown publisher" warning | Shown until the app is code-signed | Not shown |
| Where it installs | Program Files or your user folder | Windows keeps it in its protected apps folder |

## Things that could stop an approval

- **runFullTrust.** Needed, as explained in the certification notes. Microsoft may ask for more detail. Answer plainly.
- **FFmpeg is GPL.** The licenses and source links are in the listing and in `THIRD-PARTY-NOTICES.md`. If you ever change the FFmpeg build, update the link.
- **A crash on start.** Test step 5 before every submission.
- **A name already taken.** Pick a longer name in step 2.
