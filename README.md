# MACH1 custom app

App .NET 10 + Avalonia que ajusta o processador Ryzen de dentro do Windows, controla a tela LCD do water cooler MACH1 e as ventoinhas da placa-mãe.

![A aba da tela do water cooler](docs/imagens/tela.png)

**[Guia de uso com imagens](docs/guia.md)**: cada aba explicada, o editor de layout, as ventoinhas e a solução de problemas.

| Processador | Benchmark | Ventoinhas |
| --- | --- | --- |
| ![Processador](docs/imagens/processador.png) | ![Benchmark](docs/imagens/benchmark.png) | ![Ventoinhas](docs/imagens/ventoinhas.png) |

![Os dez temas da tela](docs/imagens/temas.png)

## O que faz

**Processador** (direto no SMU; nada é gravado na BIOS, tudo vale até o reboot)

- Curve Optimizer por núcleo (-40 a +10) e limites de PBO (PPT, TDC, EDC)
- Teste de estabilidade: carrega um núcleo por vez e zera a margem do que errar
- Perfis por jogo: uma margem para todos os núcleos enquanto um executável está aberto

**Benchmark**

- Pontuação com 1 núcleo, com todos, de cada núcleo sozinho e cópia de memória, lendo temperatura, clock efetivo e potência durante o teste
- Histórico em disco e comparação de até três testes em gráficos, com exportação em PNG
- A pontuação é uma escala própria (cerca de 1000 para um núcleo do 9800X3D); serve para comparar o mesmo PC antes e depois de um ajuste

**Ventoinhas e bomba**

- Rotação e velocidade de cada conector da placa-mãe; por conector, automático da BIOS, velocidade fixa ou curva por temperatura (do processador ou da placa de vídeo), desenhada arrastando pontos
- Mínimos de segurança (20 % ventoinha, 60 % bomba) e retorno ao automático se o processador chegar perto do limite ou ao fechar o app
- Se o app for encerrado à força com um conector sob o controle dele, o conector fica na última velocidade até o PC reiniciar; na abertura seguinte o app avisa e o mantém pela curva. Use o Sair da bandeja ou `--quit`
- Só alcança o que está ligado na placa-mãe; ventoinhas com controlador próprio (as sem fio da Lian Li, por exemplo) ficam de fora

**Tela do water cooler** (USB `1600:3005`, 720x1280)

- Página de temperaturas e sensores (CPU, GPU, uso, memória, VRAM, consumo da GPU) e página do Curve Optimizer, fixas ou alternando
- Cores à escolha, imagem, GIF ou vídeo de fundo, brilho, giro de 180° e opção de tela deitada, para o bloco montado de lado
- Vídeo usa o `ffmpeg.exe` que vem com o MACH1-LCD (ou um na pasta do app ou no PATH)
- Alerta: a tela pisca em vermelho acima de um limite de temperatura
- Layout personalizado: um editor para posicionar textos, sensores, barras, imagens e GIFs com o mouse

**Geral**

- Fica na bandeja e pode abrir junto com o Windows, aplicando a curva salva
- Atalho para reiniciar direto na tela da BIOS
- Transições e animações curtas na interface (`app/Styles/Motion.axaml`), desligadas quando o Windows está com as animações desativadas

## Requisitos

- Windows 10/11 x64, executado como administrador (o app pede elevação)
- .NET 10 SDK, só para compilar; o instalador já leva o .NET
- Driver [PawnIO](https://pawnio.eu/), que o instalador também instala. À mão:

```bash
winget install namazso.PawnIO
```

- O MACH1-LCD precisa estar fechado para o app usar a tela

## Instalar

Baixe o `MACH1-custom-app-setup-<versão>.exe` na página de releases do repositório e execute. O instalador já leva o .NET, instala o driver PawnIO se ele ainda não estiver na máquina e coloca um `ffmpeg.exe` ao lado do app para o vídeo de fundo. Na desinstalação, o PawnIO e as suas configurações em `%AppData%\Mach1CustomApp` ficam.

Para gerar o instalador a partir do código, veja `gerar-instalador.cmd` e `installer/setup.iss`. As licenças dos componentes de terceiros estão em `LICENCAS.md`.

## Em que máquinas funciona

- **Tela do water cooler, sensores e benchmark**: qualquer PC com Windows 10/11 de 64 bits.
- **Curve Optimizer, teste de estabilidade e perfis por jogo**: Ryzen da série 5000 em diante.
- **Limites de PBO**: qualquer Ryzen que a ZenStates-Core conheça, inclusive os anteriores à série 5000.
- **Intel e demais processadores**: são reconhecidos (nome, núcleos P e E, temperatura, limite térmico, clock e potência), mas sem ajustes; as partes de ajuste ficam desabilitadas, com um aviso.
- **Temperatura do processador**: exige o driver PawnIO. Em Ryzen suportado vem do SMU; nos demais (Intel, por exemplo), da LibreHardwareMonitor.
- **Placa de vídeo**: NVIDIA pela NVML; AMD e Intel pela LibreHardwareMonitor.
- A faixa de margem (-40 em Zen 4/5, -30 antes), o limite de temperatura e os limites de PBO iniciais vêm do próprio processador.
- **Tela**: testada só com a IRV360 (USB `1600:3005`, 720x1280). As outras telas da linha (IEV360, IMV360, AV360) são procuradas pelo fabricante no USB e usam a resolução informada na aba da tela; as páginas se rearranjam para tela deitada ou quadrada (ver [`docs/imagens/formatos.png`](docs/imagens/formatos.png)). Nada disso foi testado com essas telas.

### Levar para outro PC

Rode `publicar.cmd`. Ele gera a pasta `dist`, com o app e o .NET juntos (cerca de 215 MB). Copie a pasta inteira, instale o PawnIO na outra máquina e abra `Mach1CustomApp.exe` como administrador. O perfil e o histórico não vão junto: ficam em `%AppData%` de cada máquina.

## Compilar e rodar

```bash
dotnet run -c Release --project app
```

Se a pasta do projeto tiver um caminho muito longo, a compilação falha com `MSB3030`. Mova o projeto para um caminho curto ou passe `--artifacts-path C:\rtb`.

Argumentos:

- `--edit-layout` abre direto o editor de layout da tela
- As variáveis de ambiente `MACH1_GENERIC=1` (PC sem Ryzen suportado e sem NVIDIA) e `MACH1_NO_CO=1` (Ryzen sem Curve Optimizer) simulam outras máquinas, para testar esses caminhos
- `--tray` abre só na bandeja (é como a tarefa agendada chama o app)
- `--tab N` abre direto na aba N (0 a 4)
- `--quit` encerra a cópia aberta do jeito normal (devolve as ventoinhas à placa-mãe)
- `--fans <arquivo>` lista os conectores de ventoinha, sem alterar nada
- `--fan-test <conector> <porcentagem> <arquivo>` fixa um conector por 8 s, anota a rotação e devolve ao automático
- `--bench <segundos> <arquivo.json>` roda o benchmark sem janela e grava o resultado
- `--lcd-size <largura> <altura>` faz a prévia abaixo em outra resolução; com `--lcd-sideways`, como tela deitada
- `--lcd-preview <pasta> [imagem]` grava as páginas do painel em arquivos, com valores de exemplo, sem tocar em hardware

## Estrutura

- `app/` — o aplicativo (Avalonia, MVVM)
  - `Services/Machine.cs` — o que há neste PC e de onde vem cada sensor
  - `Services/CpuTuner.cs` — comandos do SMU (só Ryzen suportado)
  - `Services/FanControl.cs` — ventoinhas e bomba pela LibreHardwareMonitor
  - `Services/GenericSensors.cs` — sensores pela LibreHardwareMonitor
  - `Services/LcdDevice.cs` — protocolo da tela (documentado no topo do arquivo)
  - `Services/LcdRenderer.cs` e `LcdService.cs` — desenho do painel e envio
  - `Services/LcdBackground.cs` e `VideoDecoder.cs` — fundo com imagem, GIF ou vídeo
  - `Services/BenchmarkRunner.cs` e `EffectiveClock.cs` — benchmark e clock efetivo
  - `Controls/LayoutCanvas.cs` e `Views/LayoutEditorWindow.axaml` — editor de layout
  - `Controls/ScoreBars.cs`, `DotPlot.cs`, `TimeChart.cs` — gráficos
  - `Services/StabilityTester.cs`, `StartupGuard.cs`, `StartupTask.cs`
  - `ViewModels/MainViewModel*.cs` — uma parte por área (processador, tela, teste, jogos, inicialização)
- `docs/` — guia de uso e imagens
- `installer/` — script do instalador (Inno Setup)
- `ext/zsc/` — cópia do código da [ZenStates-Core](https://github.com/irusanov/ZenStates-Core) (GPL-3.0)

As opções e o perfil ficam em `%AppData%\Mach1CustomApp\profile.json`.

## Licença

O app referencia a ZenStates-Core, que é GPL-3.0; se for distribuir o binário, ele precisa seguir a mesma licença.
