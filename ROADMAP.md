# Roadmap

What is done, what is being worked on, and what may come next for Mercury Converter. This is a plan, not a promise: priorities change with what people ask for, and nothing here has a date.

**Have an idea or a vote?** Add it in [Discussions](https://github.com/AfridexD/MercuryConverter/discussions) (use 👍 on ideas you want). Found a bug? [Open an issue](https://github.com/AfridexD/MercuryConverter/issues/new).

## Done

- Photos, GIFs, video and audio conversion on your own PC, with nothing uploaded
- One quality slider with the expected size, and "never make a file larger than the original"
- Right-click menu in File Explorer, with a small Quick Convert window
- Presets you can save, import and export
- Previews at three sizes (or none)
- Remove location and camera details from photos, videos and songs
- Pull the sound out of a video
- Animated WebP: turn short videos and GIFs into small looping pictures
- Dark mode, following Windows or your choice
- Optional "new version available" notice (off until you turn it on)
- A notification when a long batch finishes
- Security hardening for the converter programs ([details](SECURITY.md))

## Now

- **Code signing.** Applied to SignPath Foundation, waiting for a reply. The build and release setup is ready ([how it works](docs/SIGNING.md)). Signing removes the publisher warning from Windows over time.

## Next

- **winget listing**, so you can install and update with one command
- **Translations.** The app is English only. Spanish, French, German, Portuguese, Hindi and Arabic would reach the most people. [Help with a translation](https://github.com/AfridexD/MercuryConverter/discussions) is very welcome.
- **More output formats**, such as HEIC
- **Merge photos into one PDF**, and rename files while converting
- **Pause and resume** a batch
- **Remember the list** after the app is closed

## Later

- Windows 11 modern right-click menu (today it is under "Show more options")
- A build for ARM64 laptops
- Compress to a target size ("under 8 MB")
- A page for sharing presets

## How this list is used

- Items move from Later to Next to Now as they are started.
- Things that many people ask for first get done first.
- When something ships, it moves to Done and appears in the [release notes](https://github.com/AfridexD/MercuryConverter/releases).
