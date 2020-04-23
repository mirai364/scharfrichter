# Scharfrichter
A library and toolset designed for rhythm games.

[![GitHub license](https://img.shields.io/github/license/saxxonpike/scharfrichter.svg)](LICENSE)

## Binary releases

[Windows binary releases are released here on Github.](https://github.com/SaxxonPike/scharfrichter/releases)

## How to use
This project is both a class library and a CLI toolset. If you just want the raw conversion
functionality, just include the `Scharfrichter` project. There is a couple helper method inside
`ConvertHelper` that pretty much all the command line tools use to reduce repetition in the code.

Typically all you have to do is feed it an input file. Output folders will be created automatically
in the same folder. Output folder options are planned for the future.

### optional  
If you need to include a movie when converting ".1" file, please set "MOVIEFOLDER" setting and "ISSAMEFOLDERMOVIE" to "true" in [BMS].  
If you want to change the output folder, please change "OUTPUT" setting in [BMS]. 

## What it can do

### Media conversion
- djmain HDDs (beatmania)
- twinkle HDDs (beatmaniaIIDX only)
- bemanipc files (beatmaniaIIDX only)
- SSQ (Dance Dance Revolution)
- XWB/XSB (Xbox and XAudio based files, used in later DDR games also)
  - You will also get WAV conversion *IF* you have the appropriate XAudio codec

## What you'll need
Visual Studio 2013 Community can build this project. It is free from Microsoft.
Testing on non-Windows platforms is pending.

## TODO
Whatever I feel like (or whatever you feel like contributing.) It needs a lot of cleanup.
The project's foundation isn't that well defined so there's functionality scattered out in
a few different namespaces.

## Status

### BemaniToBMS (w/ Gold, Troopers+)
Should be nearly complete unless new compression methods pop up.

### DDRPSXExtract
WIP, assume it doesn't work.

### DJMainExtract
Should work with all DJMain games. Raw images only currently.
CHD support is in the works but won't be available until MAME's Huffman
compression is ported.

You'll need to use `chdman extracthd -i input.chd -o output.raw` if you
want to convert things for now.

### FirebeatDecompress
WIP, assume it doesn't work.

### IFSExtract
Very limited testing- it might fail on IFS files from games that aren't IIDX.

### IIDXDBGenerator
Drag & Drop your music_data.bin  
If there is added to the read data, add data to onfig/BeatmaniaDB.txt

### LZDecompress, LZSS2Decompress
Unlikely these will ever be needed, but they hook into the core decompression functionality.

### Render2DXTroopers
Should work on any .1/.2DX file pair Troopers and above unless timing changes in the future.
Ideal for creating GSTs.

### TwinkleIIDXExtract
Very limited testing on raw IIDX hard drive images only.
