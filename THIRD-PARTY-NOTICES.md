# Third-party notices

Mercury Converter's own source code is under the MIT license (see [LICENSE](LICENSE)).

The installer and portable download also include the programs below. Mercury Converter runs each of them as a
separate program. It does not link to them. Each has its own license, and you must keep these notices when you
redistribute the downloads.

| Program | Used for | License | Where to get the source |
|---|---|---|---|
| [FFmpeg](https://ffmpeg.org/) (BtbN Windows build) | Video and audio conversion | GNU GPL version 3. This build is configured with `--enable-gpl --enable-version3` and includes x264 and libvpx | [ffmpeg.org](https://ffmpeg.org/download.html) and [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) |
| [ImageMagick 7](https://imagemagick.org/) | Image conversion | [ImageMagick License](https://imagemagick.org/license/) | [imagemagick.org](https://imagemagick.org/script/download.php) |
| [libjxl](https://github.com/libjxl/libjxl) (`cjxl`, `djxl`) | JPEG XL | BSD 3-Clause | [github.com/libjxl/libjxl](https://github.com/libjxl/libjxl) |

The license text for each program is included in its folder under `tools\` in the downloads
(`LICENSE.txt` for FFmpeg, `License.txt` for ImageMagick).

## Libraries used to build the app

| Library | License |
|---|---|
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) | See the license in its NuGet package |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php).

## If you redistribute the downloads

The FFmpeg build is licensed under the GPL version 3. If you give someone the installer or the zip, the GPL
requires that they can get the corresponding FFmpeg source. The links above and the scripts in this repository point
to the same builds. Read the GPL text for the exact conditions. This file is a summary, not legal advice.
