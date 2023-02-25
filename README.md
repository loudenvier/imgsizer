# imgsizer
Image (re)Sizer is a multiplatform .NET application to resize/shrink image files, either inplace or to a destination directory (preserving folder structure). It aims to be very vast and to keep as much image quality as possible. To that end it uses the amazing [MagicScaller](https://github.com/saucecontrol/PhotoSauce) high-performance, high-quality image processing pipeline for .NET library.

## Usage

```
(_)  _ __ ___     __ _   ___  (_)  ____   ___   _ __
 | | | '_ ` _ \   / _` | / __| | | |_  /  / _ \ | '__|
 | | | | | | | | | (_| | \__ \ | |  / /  |  __/ | |
 |_| |_| |_| |_|  \__, | |___/ |_| /___|  \___| |_|
                  |___/
Image(re)Sizer 2023

imgsizer 1.0.0
Copyright (C) 2023 imgsizer

  -w, --width        Image's target width

  -h, --height       Image's target height

  -d, --dest         (Default: resized) Output path/directory (if it's '.' the source files will be overwritten)

  -o, --overwrite    (Default: false) Automatically overwrites destination files

  -r, --recursive    (Default: false) Includes files inside subdirectories

  -j, --job          Allows pausing and resuming with named "jobs"

  --help             Display this help screen.

  --version          Display version information.

  source (pos. 0)    Required. Source file/pattern
  ```
