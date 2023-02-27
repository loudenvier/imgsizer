# imgsizer
Image (re)Sizer is a multiplatform .NET application to resize/shrink image files, either inplace or to a destination directory (preserving folder structure). It aims to be very vast and to keep as much image quality as possible. To that end it uses the amazing [MagicScaller](https://github.com/saucecontrol/PhotoSauce) high-performance, high-quality image processing pipeline library for .NET.

## Usage

```
  _                             _
 (_)  _ __ ___     __ _   ___  (_)  ____   ___   _ __
 | | | '_ ` _ \   / _` | / __| | | |_  /  / _ \ | '__|
 | | | | | | | | | (_| | \__ \ | |  / /  |  __/ | |
 |_| |_| |_| |_|  \__, | |___/ |_| /___|  \___| |_|
                  |___/
                            Image(re)Sizer 2023

ACCEPTED ARGUMENTS

  -w, --width        Image's target width

  -h, --height       Image's target height

  -d, --dest         (Default: resized) Output path/directory (if it's '.' performs inplace resizing)

  -o, --overwrite    (Default: false) Automatically overwrites destination files

  -r, --recursive    (Default: false) Includes files inside subdirectories

  -j, --job          Allows pausing and resuming with named "jobs"

  --whatif           Runs in simulation mode (output isn't written to disk)

  -s, --scalemode    (Default: Off) (off, favorquality, favorspeed, turbo) Defines the mode that 
                     control speed vs. quality trade-offs for high-ratio scaling operations.

  --help             Display this help screen.

  --version          Display version information.

  source (pos. 0)    Required. Source file/pattern

```
