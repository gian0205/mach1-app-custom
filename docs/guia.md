# Guia de uso do MACH1 custom app

Este guia mostra cada parte do app, com imagens da versão 1.0.1 rodando em um Ryzen 7 9800X3D com a tela IRV360. Para instalar, compilar e ver a lista de argumentos, volte ao [README](../README.md).

- [Instalação e primeira abertura](#instalação-e-primeira-abertura)
- [A janela](#a-janela)
- [Tela do water cooler](#tela-do-water-cooler)
- [Editor de layout](#editor-de-layout)
- [Processador](#processador)
- [Benchmark](#benchmark)
- [Ventoinhas e bomba](#ventoinhas-e-bomba)
- [LEDs](#leds)
- [Geral](#geral)
- [Bandeja e encerramento](#bandeja-e-encerramento)
- [Onde ficam os seus dados](#onde-ficam-os-seus-dados)
- [Solução de problemas](#solução-de-problemas)

## Instalação e primeira abertura

1. Baixe o `MACH1-custom-app-setup-<versão>.exe` na página de releases do repositório e execute.
2. O Windows SmartScreen pode avisar, porque o instalador não é assinado: clique em "Mais informações" e depois em "Executar assim mesmo".
3. O instalador copia o app com o .NET junto, instala o driver PawnIO se ele ainda não estiver na máquina e coloca um `ffmpeg.exe` ao lado do app. Ele oferece três opções: atalho na área de trabalho, abrir junto com o Windows e instalar o OpenRGB 1.0, usado pela aba LEDs. A do OpenRGB só aparece se ele ainda não estiver na máquina.
4. Feche o MACH1-LCD do fabricante antes de abrir o app. Os dois programas não conseguem usar a tela ao mesmo tempo.
5. Abra o MACH1 custom app. O Windows pede permissão de administrador a cada abertura, porque o app fala direto com o processador e com o chip de ventoinhas da placa-mãe.

## A janela

O topo mostra o nome do processador, a geração, o número de núcleos e a temperatura atual do processador e da placa de vídeo. Abaixo ficam as seis abas. A linha no rodapé mostra a última ação feita pelo app.

O app abre na aba **Tela do water cooler**.

## Tela do water cooler

![Aba da tela do water cooler](imagens/tela.png)

À esquerda fica a prévia do que está sendo enviado para a tela, atualizada uma vez por segundo. Embaixo dela o app informa quantos quadros por segundo está enviando e o tamanho de cada um.

**Painel**

- **Mostrar o painel na tela** liga e desliga o envio.
- **Brilho** vai de 5 a 100. O mínimo existe para a tela não ficar apagada por engano.
- **Girar a imagem 180°** serve para quem montou o bloco de cabeça para baixo.

**Modelo da tela**

O app procura a tela pelo fabricante no USB e mostra o que encontrou. A IRV360 usa 720 x 1280. Para as outras telas da linha (IEV360, IMV360, AV360), informe a resolução do modelo: as páginas se rearranjam para tela em pé, deitada ou quadrada.

![As páginas em outros formatos de tela](imagens/formatos.png)

Só a IRV360 foi testada em hardware. As outras resoluções foram verificadas pela prévia.

**Tela deitada**

Quem montou o bloco de lado marca **Tela deitada (bloco montado de lado)**. A resolução continua sendo a do painel (720 x 1280 na IRV360); o app desenha as páginas em paisagem e gira 90° no envio. A mudança vale na próxima abertura do app.

![A opção de tela deitada, com a prévia em paisagem](imagens/tela-deitada.png)

- Se a imagem sair de cabeça para baixo, marque ou desmarque **Girar a imagem 180°**.
- Um layout personalizado feito com a tela em pé não acompanha a mudança. Abra o editor e reposicione os elementos, ou use **Recomeçar do modelo**.
- Esta opção foi conferida pela prévia e pelo quadro enviado, em uma IRV360 montada em pé. Ainda não foi vista em um bloco montado de lado.

**Conteúdo**

Há páginas prontas, estilos de painel e uma página livre:

- **Temperaturas e sensores**: temperatura do processador e da placa de vídeo, uso, memória, memória de vídeo e consumo da placa de vídeo.
- **Curve Optimizer**: a margem e o clock de cada núcleo.
- **Alternar entre as duas**: troca de página a cada tantos segundos.
- **Layout personalizado**: o que você montar no [editor de layout](#editor-de-layout).
- **Gráfico de temperatura e uso**: as linhas dos últimos três minutos, do processador e da placa de vídeo.
- **Medidores**: arcos para as duas temperaturas e, menores, uso do processador, uso da placa de vídeo e memória.
- **Relógio**: a hora grande, a data e as duas temperaturas embaixo.
- **Minimalista**: só a temperatura do processador, enorme, com a linha dos últimos minutos.
- **Blocos com minigráficos**: seis blocos, cada um com o valor e a linha dos últimos minutos.
- **Clima**: o tempo agora, as próximas horas e os próximos dias, pela Open-Meteo. Veja [Clima](#clima), abaixo.

Os rótulos e valores pequenos de todas as páginas são desenhados em letra maior e mais grossa, para ler a tela a média distância.

![Página de temperaturas, página do Curve Optimizer e o alerta de temperatura](imagens/paginas-da-tela.png)

A imagem acima mostra a página de temperaturas, a página do Curve Optimizer e a tela durante um alerta de temperatura.

![Os cinco estilos de painel, no tema padrão e em um tema neon](imagens/estilos.png)

Os cinco estilos, em cima no tema Ardósia e embaixo no Neon ciano, com leituras de exemplo: gráfico, medidores, relógio, minimalista e blocos. Eles usam as cores do tema escolhido, o efeito neon, as cores da temperatura e o fundo com imagem ou vídeo, e se rearranjam em tela deitada ou quadrada. Os gráficos começam vazios e só ocupam a largura toda depois de três minutos com o app aberto.

**Aparência**

![Temas, cores e conteúdo](imagens/tela-aparencia.png)

Clique em um dos temas prontos ou escolha a cor do fundo, do texto e das temperaturas e barras uma a uma. **Voltar ao visual padrão** desfaz tudo.

![Os dez temas de cor](imagens/temas.png)

Há também sete temas neon: Neon ciano, Neon rosa, Neon verde, Neon roxo, Neon azul, Neon carbono e Synthwave. Neles as barras são desenhadas como tubos de neon acesos, com halo de luz e o miolo mais claro, e as temperaturas brilham na mesma cor.

![Os sete temas neon](imagens/temas-neon.png)

O efeito é uma opção à parte, **Efeito neon nas barras e temperaturas**, logo abaixo das cores. Dá para ligá-lo com qualquer combinação de cores; fica melhor com fundo bem escuro e uma cor viva. As capturas da janela neste guia são de antes desses temas e não mostram a opção.

**Fundo com imagem, GIF ou vídeo**

![Fundo, cores da temperatura e alerta](imagens/tela-fundo.png)

**Escolher imagem, GIF ou vídeo de fundo** aceita imagens, GIFs animados e vídeos como MP4. Com um arquivo escolhido aparecem o botão **Remover** e o controle **Opacidade da cor de fundo sobre a imagem ou o vídeo**: em 0 % aparece só a imagem ou o vídeo; em 100 %, só a cor de fundo. Um valor intermediário mantém os números legíveis sobre um fundo movimentado.

O vídeo depende do `ffmpeg.exe`. O instalador já coloca um ao lado do app; quem compila pelo código precisa de um na pasta do app, no PATH ou na instalação do MACH1-LCD.

A tela recebe os quadros por USB a cerca de 1,4 MB por segundo. Com fundo animado o app reduz a qualidade de cada quadro até caber nesse limite, então um vídeo muito detalhado fica com menos nitidez ou menos quadros por segundo.

**Cores da temperatura**

Com a opção ligada, a temperatura muda de cor ao passar de dois limites: atenção e crítico. Você define os dois limites para o processador e para a placa de vídeo, e as duas cores. **Voltar ao automático** usa o limite do seu processador: atenção 20 °C antes dele e crítico 7 °C antes.

**Clima**

![A página do clima, no tema padrão, em um tema neon e com a tela deitada](imagens/clima.png)

A página **Clima** mostra o tempo agora (desenho, temperatura, descrição, máxima e mínima), sensação térmica, umidade, vento e chance de chuva, as próximas horas de 3 em 3 e os próximos dias, com a faixa entre a mínima e a máxima de cada um. Os dados vêm da [Open-Meteo](https://open-meteo.com/), que é gratuita e não pede cadastro, e são atualizados a cada 15 minutos.

Na seção **Clima** da aba da tela:

- **Buscar a previsão do tempo** liga a busca. Escolher a página Clima em **Conteúdo** já liga sozinho.
- **Localização automática** usa a localização do Windows (Configurações > Privacidade > Localização, com o acesso de aplicativos ligado). Se ela estiver desligada e a opção logo abaixo estiver marcada, o app usa a localização aproximada pelo endereço de internet, pelo ipinfo.io: costuma acertar a cidade, mas às vezes cai na cidade do provedor.
- **Cidade escolhida**: digite o nome, clique em **Buscar** e escolha na lista.
- A linha embaixo mostra o lugar usado, a temperatura e a hora da última busca. **Atualizar agora** busca de novo a localização e a previsão.

O layout personalizado ganhou três sensores com esses dados: **Temperatura lá fora**, **Tempo agora** e **Umidade lá fora**.

Com a localização do Windows, a previsão vem só com as coordenadas, e o topo da página mostra "Clima" no lugar do nome da cidade.

**Alerta de temperatura**

Com a opção ligada, a tela pisca em vermelho quando o processador ou a placa de vídeo passa do limite que você definir.

## Editor de layout

![Editor de layout](imagens/editor-layout.png)

Abra pelo botão **Editar o layout personalizado** na aba da tela. O editor troca o conteúdo da tela para **Layout personalizado**.

- **Adicionar** cria um texto, um sensor, uma barra ou uma imagem ou GIF.
- Clique em um elemento para selecionar e arraste para mover. O quadradinho no canto inferior direito redimensiona.
- As setas do teclado movem 1 pixel; com Shift, 10.
- A lista **Elementos** mostra a ordem de desenho: o de baixo fica por cima. Use **Trazer para frente**, **Enviar para trás**, **Duplicar** e **Remover**.
- **Recomeçar do modelo** apaga o layout atual e volta ao modelo inicial.

O layout é gravado ao fechar a janela do editor. A cor, a imagem ou o vídeo de fundo continuam sendo os da aba da tela.

## Processador

![Aba do processador](imagens/processador.png)

Esta aba fala direto com o SMU do processador. Nada é gravado na BIOS: tudo vale até o próximo reboot. Se o PC travar com uma margem agressiva demais, basta reiniciar.

**Curve Optimizer** (Ryzen da série 5000 em diante)

- Cada núcleo tem uma margem, de -40 a +10 em Zen 4 e Zen 5 e de -30 a +10 nos anteriores. Margem negativa reduz a tensão do núcleo: menos calor e mais clock sustentado.
- A barra mostra a margem a aplicar; o traço claro, a que está em vigor no processador. As colunas à direita mostram a margem em vigor e o clock de cada núcleo.
- **Aplicar** envia a curva. **Zerar curva** volta todos a zero. **Reler** lê o que está em vigor. **Preencher** copia um valor para todos os núcleos.

**Limites de PBO**

Marque **Alterar limites ao aplicar** para mudar PPT, TDC e EDC. Os valores iniciais vêm do próprio processador. Depois de alterados, eles só voltam ao valor da BIOS no reboot.

**Teste de estabilidade**

Carrega um núcleo por vez com a curva em vigor e confere os cálculos. Se um núcleo errar, a margem dele é zerada na hora. É uma triagem rápida e não substitui dias de uso.

**Perfis por jogo**

Informe o nome do executável, por exemplo `cs2.exe`, e uma margem. Enquanto o executável estiver aberto, essa margem vale para todos os núcleos. Ao fechar o jogo, volta a curva da janela. Funciona também com o app na bandeja.

**Em outros processadores**

- Ryzen anterior à série 5000: só os limites de PBO. A curva, o teste e os perfis por jogo ficam desabilitados, com um aviso.
- Intel e demais: a aba fica desabilitada. O restante do app funciona.

## Benchmark

![Aba de benchmark](imagens/benchmark.png)

O benchmark mede o PC como ele está agora. A ideia é rodar uma vez antes e outra depois de mudar a curva ou a BIOS e comparar.

1. Feche jogos e programas pesados.
2. Dê um nome ao teste. Sem nome, ele leva o da curva em vigor, como "Curva -15".
3. Escolha os segundos por etapa e se quer testar cada núcleo sozinho e a cópia de memória.
4. Clique em **Rodar benchmark**.

Os testes ficam salvos na lista. Marque até três para comparar:

- **1 núcleo** e **todos os núcleos**, em pontos, com a diferença em porcentagem para o primeiro teste da legenda.
- **Cópia de memória**, em GB/s.
- **Cada núcleo sozinho**. O eixo deste gráfico não começa no zero, para as diferenças entre núcleos aparecerem.
- **Temperatura** e **clock efetivo** ao longo do teste, com as etapas marcadas.

A pontuação é uma escala própria do app, com cerca de 1000 pontos para um núcleo do 9800X3D. Ela serve para comparar o mesmo PC antes e depois de um ajuste, e não para comparar com outros programas de benchmark.

**Salvar os gráficos como imagem** grava a comparação em um PNG.

## Ventoinhas e bomba

![Aba de ventoinhas com uma curva por temperatura](imagens/ventoinhas.png)

Cada bloco é um conector da placa-mãe com algo ligado. O app mostra a rotação e a velocidade atual, e você escolhe quem manda em cada conector:

- **Automático da placa-mãe**: a curva da BIOS. É o padrão.
- **Velocidade fixa**: o conector fica na porcentagem escolhida.
- **Curva por temperatura**: o app ajusta a velocidade a cada segundo, seguindo a temperatura do processador ou da placa de vídeo.

**Descobrir qual conector é qual**

A placa-mãe só chama os conectores de Fan #1, Fan #2 e assim por diante. Fixe um de cada vez em 100 %, veja o que acelera e dê um nome a ele no campo do topo do bloco. Marque **Este conector é a bomba** no conector da bomba.

**Desenhar a curva**

Arraste os pontos: para os lados muda a temperatura, para cima e para baixo a velocidade. A linha vertical mostra a temperatura de agora. Antes do primeiro ponto e depois do último a velocidade fica constante. **Curva sugerida** volta a uma curva inicial.

A curva reage rápido quando esquenta e devagar quando esfria, para a ventoinha não ficar acelerando e desacelerando.

**Proteções**

- Ventoinha não desce de 20 % e bomba não desce de 60 %.
- Se o processador chegar a 8 °C do limite dele, o app devolve tudo ao automático e só retoma depois de esfriar.
- Ao sair do app, tudo volta ao automático.
- **Voltar tudo ao automático** faz isso na hora.

**Limites**

- A velocidade fixa e a curva só funcionam com o app aberto ou na bandeja.
- O app só alcança o que está ligado na placa-mãe. Ventoinhas com controlador próprio, como as sem fio da Lian Li, ficam de fora, a menos que o controlador esteja configurado para seguir o sinal da placa-mãe.
- Se o app for encerrado à força com um conector sob o controle dele, o conector fica na última velocidade até o PC reiniciar. Veja [Bandeja e encerramento](#bandeja-e-encerramento).

## LEDs

![Aba de LEDs](imagens/leds.png)

Esta aba controla os LEDs RGB do PC por meio do [OpenRGB](https://openrgb.org/). O app não fala direto com os LEDs: ele manda a cor para o OpenRGB, que conhece a placa-mãe, a memória, a placa de vídeo e os controladores. Na imagem acima, o OpenRGB 1.0 encontrou os dois módulos de memória, a placa-mãe e um controle DualSense, que foi deixado de fora.

**O OpenRGB**

O instalador do app oferece o OpenRGB 1.0. Essa versão roda como serviço do Windows e já sobe com o servidor SDK ligado, então não é preciso abrir nem configurar nada: basta marcar **Controlar os LEDs** no app. Se o serviço estiver parado, o app pede ao Windows para iniciá-lo.

Para ver o que o OpenRGB encontrou no seu PC, abra o programa dele pelo menu Iniciar. O que ele não enxerga, o app também não controla.

Quem usa uma versão anterior à 1.0 precisa abrir o OpenRGB como administrador e, na aba **SDK Server**, clicar em **Start Server**. A porta padrão é 6742.

**No app**

- **Controlar os LEDs** liga o envio. Se o OpenRGB não responder, o app tenta de novo a cada 5 segundos.
- **Cor fixa**: a cor e o brilho escolhidos.
- **Cor de destaque da tela do water cooler**: os LEDs acompanham o tema da tela.
- **Conforme a temperatura do processador**: os LEDs ficam na cor escolhida e passam aos poucos para as cores de atenção e crítica da aba da tela quando o processador esquenta.
- **Dispositivos**: desmarque o que deve ficar de fora.

**Limites**

- Todos os LEDs de um dispositivo recebem a mesma cor; não há efeitos nem cor por LED.
- Dois programas mandando cor para o mesmo LED brigam entre si. Feche ou desative no L-Connect, no SignalRGB e nos programas da placa-mãe o que for controlado pelo app.
- Ao desligar a opção ou fechar o app, os LEDs ficam na última cor enviada.
- Testado com o OpenRGB 1.0, memória XPG (ENE DRAM) e placa-mãe ASRock X870 Pro RS. O OpenRGB não encontrou as ventoinhas sem fio da Lian Li nesse PC, então elas ficam de fora.

## Geral

![Aba geral](imagens/geral.png)

- **Salvar perfil** guarda as margens e os limites da aba Processador. As outras opções são gravadas sozinhas quando você as muda.
- **Abrir com o Windows, na bandeja** cria uma tarefa agendada que abre o app a cada logon, já com permissão de administrador.
- **Aplicar a curva do perfil salvo ao abrir com o Windows** só deve ser ligada depois de alguns dias de uso estável. Se o PC cair até 3 minutos depois da aplicação automática, a curva deixa de ser aplicada até você salvar um perfil novo.
- **Ao fechar a janela, continuar na bandeja** mantém a tela, as ventoinhas e os perfis por jogo funcionando.
- **Reiniciar na BIOS** reinicia direto na tela da BIOS, para você digitar lá os valores que ficaram estáveis e torná-los permanentes.

## Bandeja e encerramento

Com o app na bandeja, clique com o botão direito no ícone: **Abrir** mostra a janela e **Sair** encerra.

Encerre sempre por **Sair** ou por linha de comando:

```bash
Mach1CustomApp.exe --quit
```

Não finalize o processo pelo Gerenciador de Tarefas. Ao sair do jeito normal o app devolve as ventoinhas e a bomba à placa-mãe; encerrado à força, o conector que estava em velocidade fixa ou curva fica na última velocidade até o PC reiniciar. Na abertura seguinte o app avisa e mantém esse conector sob controle.

## Onde ficam os seus dados

Tudo fica em `%AppData%\Mach1CustomApp`:

| Arquivo | Conteúdo |
| --- | --- |
| `profile.json` | Curva, limites, opções da tela, layout, ventoinhas e jogos |
| `benchmarks.json` | Histórico de testes |
| `crash.log` | Registro de erros, se houver |

Desinstalar o app não apaga essa pasta, o driver PawnIO nem o OpenRGB.

## Solução de problemas

**"Tela não encontrada"**

Feche o MACH1-LCD do fabricante e confira o cabo USB do bloco. O app tenta de novo sozinho a cada 5 segundos.

**A tela está preta ou sem imagem**

Confira o **Brilho** e se **Mostrar o painel na tela** está marcado. Em uma tela que não seja a IRV360, confira a resolução em **Modelo da tela**: com a resolução errada a imagem não aparece ou sai cortada.

**A imagem está de cabeça para baixo**

Marque ou desmarque **Girar a imagem 180°**.

**A imagem está de lado**

Marque ou desmarque **Tela deitada** em **Modelo da tela** e abra o app de novo.

**O vídeo de fundo está lento**

O limite é o USB da tela. Use um vídeo com menos detalhe e movimento.

**O vídeo de fundo não aparece**

Falta o `ffmpeg.exe`. Coloque um na pasta do app.

**A temperatura do processador não aparece, ou a aba Processador está desabilitada**

Instale o driver PawnIO e abra o app de novo:

```bash
winget install namazso.PawnIO
```

**O PC travou depois de aplicar uma curva**

Reinicie: a curva não fica gravada. Use uma margem menos negativa no núcleo que falhou. Se a aplicação automática estava ligada, o app a bloqueia sozinho depois de uma queda logo após o logon.

**Uma ventoinha ficou presa em uma velocidade**

O app foi encerrado à força. Reinicie o PC para a placa-mãe retomar o conector.

**Uma ventoinha não aparece**

Ela não está ligada a um conector da placa-mãe, ou o chip de monitoramento da placa não é reconhecido pela LibreHardwareMonitor.

**Os LEDs não mudam de cor**

Confira a mensagem no topo da aba LEDs. Abra o OpenRGB pelo menu Iniciar e veja se ele próprio lista o dispositivo e consegue mudar a cor dele. Feche outros programas de LED que estejam controlando a mesma peça.

**O app fechou sozinho**

Veja o `crash.log` em `%AppData%\Mach1CustomApp` e abra uma issue no repositório com o conteúdo dele.
