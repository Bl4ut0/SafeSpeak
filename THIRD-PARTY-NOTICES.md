# SafeSpeak third-party notices

SafeSpeak is proprietary source-visible software, but it redistributes the
third-party components below under their own licenses. The SafeSpeak license
does not replace, restrict, or relicense these components.

The release builder includes this notice, the Apache-2.0 text, and the exact
ONNX Runtime license and third-party notice files in every ZIP and MSIX.

## MIT-licensed components

- CommunityToolkit.Mvvm 8.4.2 - Microsoft -
  <https://github.com/CommunityToolkit/dotnet>
- KokoroSharp and KokoroSharp.CPU 0.8.4 - Lyrcaxis -
  <https://github.com/Lyrcaxis/KokoroSharp>
- Microsoft.ML.OnnxRuntime and Microsoft.ML.OnnxRuntime.Managed 1.22.0 -
  Microsoft - <https://github.com/microsoft/onnxruntime>
- Microsoft.NETCore.Platforms 3.1.0, Microsoft.Win32.Registry 4.7.0,
  System.Memory 4.5.5, System.Numerics.Tensors 9.0.5,
  System.Runtime.CompilerServices.Unsafe 5.0.0,
  System.Security.AccessControl 4.7.0,
  System.Security.Principal.Windows 4.7.0, and System.Speech 8.0.0 -
  Microsoft - <https://github.com/dotnet/runtime>
- NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi,
  NAudio.WinForms, and NAudio.WinMM 2.2.1 - Mark Heath and contributors -
  <https://github.com/naudio/NAudio>
- OpenTK.Audio.OpenAL, OpenTK.Core, and OpenTK.Mathematics 5.0.0-pre.13 -
  the OpenTK team - <https://github.com/opentk/opentk>

### MIT License text

Permission is hereby granted, free of charge, to any person obtaining a copy
of the MIT-licensed software and associated documentation files (the
"Software"), to deal in the Software without restriction, including without
limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom
the Software is furnished to do so, subject to the following conditions:

The applicable copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The exact ONNX Runtime and NAudio copyright/license files are also included
in the packaged `ThirdPartyNotices` directory.

## Apache-2.0 components

- MisakiSharp 2.1.1 - Lyrcaxis -
  <https://github.com/Lyrcaxis/MisakiSharp>
- NumSharp 0.30.0 - SciSharp - <https://github.com/SciSharp/NumSharp>
- The bundled local moderation model and its accompanying files, identified
  in `src/SafeSpeak.Core/AI/Models/LocalModeration/MODEL-NOTICE.md`.

The complete Apache License 2.0 text is distributed as
`ThirdPartyNotices/Apache-2.0.txt` and with the moderation model as
`Models/Moderation/LICENSE.apache-2.0.txt`.

### MisakiSharp notice

MisakiSharp is copyright 2026 Lyrcaxis and is a native C# port of hexgrad's
Apache-2.0 misaki project. Its upstream notice identifies lineage from
PaddleSpeech, jieba, pypinyin, spaCy, num2words, phonemizer, cutlet,
Convert-Numbers-to-Japanese, MeCab, UniDic 3.1.0, and misaki's English and
Japanese lexicons. The authoritative notice is at
<https://github.com/Lyrcaxis/MisakiSharp/blob/main/NOTICE>.

### OpenTK third-party notice

OpenTK's Half-to-Single and Single-to-Half conversions are based on OpenEXR
source code. Copyright (c) 2002 Industrial Light & Magic, a division of Lucas
Digital Ltd. LLC. All rights reserved. Redistribution and use in source and
binary forms, with or without modification, are permitted if source
redistributions retain the copyright notice, conditions, and disclaimer;
binary redistributions reproduce them in accompanying documentation; and the
names of Industrial Light & Magic and its contributors are not used for
endorsement without permission. The software is provided "as is," without
warranty, and its owners and contributors disclaim liability for damages.
The authoritative notice is at
<https://github.com/opentk/opentk/blob/master/THIRD_PARTIES.md>.

## Other bundled data and models

Model and dataset terms are documented in the model-specific notice files.
They are not covered by the SafeSpeak proprietary license. Review those terms
before replacing or redistributing any model, tokenizer, voice embedding, or
dataset-derived asset.
