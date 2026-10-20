# Componentes de terceiros

O MACH1 custom app usa ou acompanha os componentes abaixo. Cada um segue a licença do próprio autor.

| Componente | Para quê | Licença |
|---|---|---|
| [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) | Comandos ao SMU de processadores Ryzen (cópia do código em `ext/zsc`) | GPL-3.0 |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Ventoinhas e sensores de máquinas sem Ryzen suportado ou sem NVIDIA | MPL-2.0 |
| [PawnIO](https://pawnio.eu/) | Driver de acesso ao hardware; o instalador roda o instalador oficial dele | GPL-2.0 com exceções do autor |
| [FFmpeg](https://ffmpeg.org/) (build "essentials" de gyan.dev) | Decodificar vídeo de fundo da tela; acompanha o instalador como `ffmpeg.exe` | GPL-3.0 |
| [Avalonia](https://avaloniaui.net/) | Interface | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Desenho do painel da tela | MIT |
| [OpenRGB](https://openrgb.org/) 1.0 | Controle dos LEDs RGB; o instalador oferece o instalador oficial dele, sem alteração. Código-fonte: https://gitlab.com/CalcProgrammer1/OpenRGB (tag `release_1.0`) | GPL-2.0 ou posterior |
| [OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET) | Cliente do servidor SDK do OpenRGB, para os LEDs | MIT |
| [Open-Meteo](https://open-meteo.com/) | Previsão do tempo e busca de cidades, pela internet, para a página do clima (uso não comercial, sem chave) | Dados em CC BY 4.0 |
| [ipinfo.io](https://ipinfo.io/) | Localização aproximada pelo endereço de internet, só quando a do Windows está desligada e a opção está marcada | Serviço de terceiros |
| [HidSharp](https://www.zer7.com/software/hidsharp) | Comunicação USB com a tela | Apache-2.0 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | Estrutura dos view models | MIT |
| .NET | Plataforma; acompanha o instalador | MIT |

Como o app incorpora a ZenStates-Core (GPL-3.0), o MACH1 custom app inteiro é distribuído sob a GPL-3.0 (arquivo `LICENSE`). O código-fonte está em https://github.com/gian0205/mach1-app-custom.
