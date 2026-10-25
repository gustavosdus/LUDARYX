using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Localização centralizada da interface. Os textos-base são mantidos em pt-BR
/// para que futuras edições manuais continuem simples e previsíveis.
/// </summary>
public static class LocalizationService
{
    public const string PortugueseBrazil = "pt-BR";
    public const string PortuguesePortugal = "pt-PT";
    public const string EnglishUnitedStates = "en-US";
    public const string EnglishUnitedKingdom = "en-GB";
    // es-419 remains the persisted compatibility code; its translation column uses
    // Mexican Spanish as the baseline for Latin-American wording.
    public const string SpanishLatinAmerica = "es-419";
    public const string SpanishEurope = "es-ES";
    public const string JapaneseJapan = "ja-JP";

    public static string CurrentLanguage { get; private set; } = PortugueseBrazil;

    public static IReadOnlyList<(string Code, string Name)> SupportedLanguages { get; } =
    [
        (PortugueseBrazil, "Português (Brasil)"),
        (PortuguesePortugal, "Português (Portugal)"),
        (EnglishUnitedStates, "English (US)"),
        (EnglishUnitedKingdom, "English (UK)"),
        (SpanishLatinAmerica, "Español (Latinoamérica — México)"),
        (SpanishEurope, "Español (España)"),
        (JapaneseJapan, "日本語")
    ];

    private sealed record Translation(
        string PtBr,
        string PtPt,
        string EnUs,
        string EnGb,
        string Es419,
        string EsEs);

    private sealed record SupplementalTranslation(
        string PtBr,
        string PtPt,
        string EnUs,
        string EnGb,
        string Es419,
        string EsEs,
        string JaJp);

    private static readonly SupplementalTranslation[] SupplementalTranslations =
    [
        new("Atualizar biblioteca", "Atualizar biblioteca", "Refresh library", "Refresh library", "Actualizar biblioteca", "Actualizar biblioteca", "ライブラリを更新"),
        new("Estatísticas", "Estatísticas", "Statistics", "Statistics", "Estadísticas", "Estadísticas", "統計"),
        new("Buscar nas configurações", "Pesquisar nas definições", "Search settings", "Search settings", "Buscar en configuración", "Buscar en configuración", "設定を検索"),
        new("Pesquisar", "Pesquisar", "Search", "Search", "Buscar", "Buscar", "検索"),
        new("Buscar jogos", "Pesquisar jogos", "Search games", "Search games", "Buscar juegos", "Buscar juegos", "ゲームを検索"),
        new("Backup", "Cópia de segurança", "Backup", "Backup", "Copia de seguridad", "Copia de seguridad", "バックアップ"),
        new("Jogo manual", "Jogo manual", "Manual game", "Manual game", "Juego manual", "Juego manual", "手動ゲーム"),
        new("Nome exibido na biblioteca.", "Nome apresentado na biblioteca.", "Name shown in the library.", "Name shown in the library.", "Nombre mostrado en la biblioteca.", "Nombre mostrado en la biblioteca.", "ライブラリに表示する名前です。"),
        new("Use perfis para argumentos ou modos diferentes.", "Use perfis para argumentos ou modos diferentes.", "Use profiles for different arguments or modes.", "Use profiles for different arguments or modes.", "Usa perfiles para argumentos o modos diferentes.", "Usa perfiles para argumentos o modos diferentes.", "引数や起動モードごとにプロファイルを使えます。"),
        new("Identificação deste perfil.", "Identificação deste perfil.", "Identifier for this profile.", "Identifier for this profile.", "Identificador de este perfil.", "Identificador de este perfil.", "このプロファイルの名前です。"),
        new("Arquivo principal do jogo.", "Ficheiro principal do jogo.", "Main game executable.", "Main game executable.", "Ejecutable principal del juego.", "Ejecutable principal del juego.", "ゲームのメイン実行ファイルです。"),
        new("Argumentos de inicialização", "Argumentos de inicialização", "Launch arguments", "Launch arguments", "Argumentos de inicio", "Argumentos de inicio", "起動引数"),
        new("Parâmetros adicionais (opcional).", "Parâmetros adicionais (opcional).", "Additional parameters (optional).", "Additional parameters (optional).", "Parámetros adicionales (opcional).", "Parámetros adicionales (opcional).", "追加パラメーター（任意）。"),
        new("URI de inicialização", "URI de inicialização", "Launch URI", "Launch URI", "URI de inicio", "URI de inicio", "起動URI"),
        new("Comando URI personalizado (opcional).", "Comando URI personalizado (opcional).", "Custom URI command (optional).", "Custom URI command (optional).", "Comando URI personalizado (opcional).", "Comando URI personalizado (opcional).", "カスタムURIコマンド（任意）。"),
        new("Pasta de trabalho", "Pasta de trabalho", "Working directory", "Working directory", "Carpeta de trabajo", "Carpeta de trabajo", "作業フォルダー"),
        new("Diretório de trabalho do jogo (opcional).", "Diretório de trabalho do jogo (opcional).", "Game working directory (optional).", "Game working directory (optional).", "Directorio de trabajo del juego (opcional).", "Directorio de trabajo del juego (opcional).", "ゲームの作業フォルダー（任意）。"),
        new("Privilégios", "Privilégios", "Privileges", "Privileges", "Privilegios", "Privilegios", "権限"),
        new("Use apenas quando o jogo exigir.", "Use apenas quando o jogo exigir.", "Use only when the game requires it.", "Use only when the game requires it.", "Úsalo solo cuando el juego lo requiera.", "Úsalo solo cuando el juego lo requiera.", "ゲームで必要な場合のみ使用してください。"),
        new("Ícone personalizado", "Ícone personalizado", "Custom icon", "Custom icon", "Icono personalizado", "Icono personalizado", "カスタムアイコン"),
        new("Imagem ou ícone do jogo (opcional).", "Imagem ou ícone do jogo (opcional).", "Game image or icon (optional).", "Game image or icon (optional).", "Imagen o icono del juego (opcional).", "Imagen o icono del juego (opcional).", "ゲーム画像またはアイコン（任意）。"),
        new("Crie perfis diferentes para iniciar o mesmo jogo com argumentos distintos, por exemplo Normal, Sem mods ou Modo janela. O perfil selecionado será usado ao iniciar.", "Crie perfis diferentes para iniciar o mesmo jogo com argumentos distintos, por exemplo Normal, Sem mods ou Modo janela. O perfil selecionado será usado ao iniciar.", "Create different profiles to launch the same game with different arguments, for example Normal, No mods, or Windowed mode. The selected profile will be used when launching.", "Create different profiles to launch the same game with different arguments, for example Normal, No mods, or Windowed mode. The selected profile will be used when launching.", "Crea perfiles distintos para iniciar el mismo juego con argumentos diferentes, por ejemplo Normal, Sin mods o Modo ventana. Se usará el perfil seleccionado al iniciar.", "Crea perfiles distintos para iniciar el mismo juego con argumentos diferentes, por ejemplo Normal, Sin mods o Modo ventana. Se usará el perfil seleccionado al iniciar.", "同じゲームを異なる引数で起動するために、通常、MODなし、ウィンドウモードなど複数のプロファイルを作成できます。起動時には選択中のプロファイルが使われます。"),
        new("Edite informações locais sem alterar os arquivos originais do jogo.", "Edite informações locais sem alterar os ficheiros originais do jogo.", "Edit local information without changing the original game files.", "Edit local information without changing the original game files.", "Edita información local sin cambiar los archivos originales del juego.", "Edita información local sin cambiar los archivos originales del juego.", "ゲーム本体のファイルを変更せずにローカル情報を編集します。"),
        new("Nome do jogo", "Nome do jogo", "Game name", "Game name", "Nombre del juego", "Nombre del juego", "ゲーム名"),
        new("Ano de lançamento", "Ano de lançamento", "Release year", "Release year", "Año de lanzamiento", "Año de lanzamiento", "発売年"),
        new("Classificação indicativa do sistema correspondente ao idioma atual", "Classificação indicativa do sistema correspondente ao idioma atual", "Age rating for the system associated with the current language", "Age rating for the system associated with the current language", "Clasificación por edad del sistema correspondiente al idioma actual", "Clasificación por edad del sistema correspondiente al idioma actual", "現在の言語に対応する年齢レーティング"),
        new("As imagens escolhidas são copiadas para a pasta do LUDARYX.", "As imagens escolhidas são copiadas para a pasta do LUDARYX.", "Selected images are copied to the LUDARYX folder.", "Selected images are copied to the LUDARYX folder.", "Las imágenes elegidas se copian a la carpeta de LUDARYX.", "Las imágenes elegidas se copian a la carpeta de LUDARYX.", "選択した画像はLUDARYXフォルダーにコピーされます。"),
        new("Busque e selecione artes para este jogo.", "Pesquise e selecione artes para este jogo.", "Search and select artwork for this game.", "Search and select artwork for this game.", "Busca y selecciona imágenes para este juego.", "Busca y selecciona imágenes para este juego.", "このゲームの画像を検索して選択します。"),
        new("Escolha manualmente qual jogo do SteamGridDB corresponde a este jogo.", "Escolha manualmente qual jogo do SteamGridDB corresponde a este jogo.", "Manually choose which SteamGridDB game matches this game.", "Manually choose which SteamGridDB game matches this game.", "Elige manualmente qué juego de SteamGridDB corresponde a este juego.", "Elige manualmente qué juego de SteamGridDB corresponde a este juego.", "このゲームに対応するSteamGridDBのゲームを手動で選択します。"),
        new("Use a API Key para buscar capas verticais e horizontais, inclusive de jogos de outros launchers.", "Use a chave API para procurar capas verticais e horizontais, incluindo jogos de outros launchers.", "Use the API key to fetch vertical and horizontal artwork, including games from other launchers.", "Use the API key to fetch vertical and horizontal artwork, including games from other launchers.", "Usa la clave API para buscar carátulas verticales y horizontales, incluso de juegos de otros launchers.", "Usa la clave API para buscar carátulas verticales y horizontales, incluso de juegos de otros launchers.", "APIキーを使って、他のランチャーのゲームを含む縦・横のアートワークを取得します。"),
        new("API Key do SteamGridDB", "Chave API do SteamGridDB", "SteamGridDB API key", "SteamGridDB API key", "Clave API de SteamGridDB", "Clave API de SteamGridDB", "SteamGridDB APIキー"),
        new("Verifique novas versões publicadas no GitHub.", "Verifique novas versões publicadas no GitHub.", "Check for new versions published on GitHub.", "Check for new versions published on GitHub.", "Comprueba nuevas versiones publicadas en GitHub.", "Comprueba nuevas versiones publicadas en GitHub.", "GitHubで公開された新しいバージョンを確認します。"),
        new("Limite do cache (MB)", "Limite da cache (MB)", "Cache limit (MB)", "Cache limit (MB)", "Límite de caché (MB)", "Límite de caché (MB)", "キャッシュ上限 (MB)"),
        new("O backup pode preservar configurações, favoritos, jogos manuais e artes personalizadas. O cache automático de capas é regenerável.", "A cópia de segurança pode preservar definições, favoritos, jogos manuais e artes personalizadas. A cache automática de capas pode ser regenerada.", "Backups can preserve settings, favourites, manual games, and custom artwork. The automatic artwork cache can be rebuilt.", "Backups can preserve settings, favourites, manual games, and custom artwork. The automatic artwork cache can be rebuilt.", "La copia de seguridad puede conservar configuración, favoritos, juegos manuales y arte personalizado. La caché automática de carátulas se puede regenerar.", "La copia de seguridad puede conservar configuración, favoritos, juegos manuales y arte personalizado. La caché automática de carátulas se puede regenerar.", "バックアップには設定、お気に入り、手動ゲーム、カスタムアートを保存できます。自動アートキャッシュは再生成できます。"),
        new("Mostra os clientes encontrados, status e detalhes de diagnóstico.", "Mostra os clientes encontrados, estado e detalhes de diagnóstico.", "Shows detected clients, status, and diagnostic details.", "Shows detected clients, status, and diagnostic details.", "Muestra los clientes detectados, su estado y detalles de diagnóstico.", "Muestra los clientes detectados, su estado y detalles de diagnóstico.", "検出されたクライアント、状態、診断情報を表示します。"),
        new("Oculte, restaure ou recupere jogos excluídos da biblioteca.", "Oculte, restaure ou recupere jogos excluídos da biblioteca.", "Hide, restore, or recover games removed from the library.", "Hide, restore, or recover games removed from the library.", "Oculta, restaura o recupera juegos eliminados de la biblioteca.", "Oculta, restaura o recupera juegos eliminados de la biblioteca.", "ライブラリから削除したゲームを非表示、復元、回復できます。"),
        new("Tempo jogado por mês", "Tempo jogado por mês", "Play time by month", "Play time by month", "Tiempo jugado por mes", "Tiempo jugado por mes", "月別プレイ時間"),
        new("Selecione um jogo", "Selecione um jogo", "Select a game", "Select a game", "Selecciona un juego", "Selecciona un juego", "ゲームを選択"),
        new("CARREGANDO...", "A CARREGAR...", "LOADING...", "LOADING...", "CARGANDO...", "CARGANDO...", "読み込み中..."),
        new("DUPLICATA", "DUPLICADO", "DUPLICATE", "DUPLICATE", "DUPLICADO", "DUPLICADO", "重複"),
        new("● JOGANDO", "● A JOGAR", "● PLAYING", "● PLAYING", "● JUGANDO", "● JUGANDO", "● プレイ中"),
        new("Jogo", "Jogo", "Game", "Game", "Juego", "Juego", "ゲーム"),
        new("Digite o PIN de 4 números para alterar a faixa etária.", "Introduza o PIN de 4 números para alterar a faixa etária.", "Enter the 4-digit PIN to change the age range.", "Enter the 4-digit PIN to change the age range.", "Ingresa el PIN de 4 dígitos para cambiar el rango de edad.", "Introduce el PIN de 4 dígitos para cambiar el rango de edad.", "年齢区分を変更するには4桁のPINを入力してください。"),
        new("PIN de 4 números", "PIN de 4 números", "4-digit PIN", "4-digit PIN", "PIN de 4 dígitos", "PIN de 4 dígitos", "4桁のPIN"),
        new("ALTERAR PIN", "ALTERAR PIN", "CHANGE PIN", "CHANGE PIN", "CAMBIAR PIN", "CAMBIAR PIN", "PINを変更"),
        new("OUTRO JOGO JÁ ESTÁ EM EXECUÇÃO", "OUTRO JOGO JÁ ESTÁ EM EXECUÇÃO", "ANOTHER GAME IS ALREADY RUNNING", "ANOTHER GAME IS ALREADY RUNNING", "YA HAY OTRO JUEGO EN EJECUCIÓN", "YA HAY OTRO JUEGO EN EJECUCIÓN", "別のゲームがすでに実行中です"),
        new("Executar dois jogos ao mesmo tempo pode aumentar o uso de CPU, GPU e memória, reduzir FPS, causar travamentos ou deixar o sistema menos responsivo.", "Executar dois jogos ao mesmo tempo pode aumentar o uso de CPU, GPU e memória, reduzir FPS, causar bloqueios ou deixar o sistema menos responsivo.", "Running two games at once can increase CPU, GPU, and memory usage, reduce FPS, cause freezes, or make the system less responsive.", "Running two games at once can increase CPU, GPU, and memory usage, reduce FPS, cause freezes, or make the system less responsive.", "Ejecutar dos juegos al mismo tiempo puede aumentar el uso de CPU, GPU y memoria, reducir los FPS, causar bloqueos o volver el sistema menos responsivo.", "Ejecutar dos juegos al mismo tiempo puede aumentar el uso de CPU, GPU y memoria, reducir los FPS, causar bloqueos o volver el sistema menos responsivo.", "2つのゲームを同時に実行すると、CPU・GPU・メモリ使用量が増え、FPS低下、フリーズ、システム応答性の低下が起こる場合があります。"),
        new("Você pode manter apenas a sessão atual ou continuar e abrir o novo jogo também.", "Pode manter apenas a sessão atual ou continuar e abrir também o novo jogo.", "You can keep only the current session or continue and open the new game too.", "You can keep only the current session or continue and open the new game too.", "Puedes mantener solo la sesión actual o continuar y abrir también el nuevo juego.", "Puedes mantener solo la sesión actual o continuar y abrir también el nuevo juego.", "現在のゲームだけを維持するか、新しいゲームも同時に開くことができます。"),
        new("MANTER JOGO ATUAL", "MANTER JOGO ATUAL", "KEEP CURRENT GAME", "KEEP CURRENT GAME", "MANTENER JUEGO ACTUAL", "MANTENER JUEGO ACTUAL", "現在のゲームを維持"),
        new("ABRIR TAMBÉM", "ABRIR TAMBÉM", "OPEN BOTH", "OPEN BOTH", "ABRIR TAMBIÉN", "ABRIR TAMBIÉN", "両方開く")
        new("Dados selecionados para restauração: {0:0.0} MB", "Dados selecionados para restauro: {0:0.0} MB", "Selected data to restore: {0:0.0} MB", "Selected data to restore: {0:0.0} MB", "Datos seleccionados para restaurar: {0:0.0} MB", "Datos seleccionados para restaurar: {0:0.0} MB", "復元対象データ: {0:0.0} MB"),
        new("Dados de origem estimados: {0:0.0} MB", "Dados de origem estimados: {0:0.0} MB", "Estimated source data: {0:0.0} MB", "Estimated source data: {0:0.0} MB", "Datos de origen estimados: {0:0.0} MB", "Datos de origen estimados: {0:0.0} MB", "推定元データ: {0:0.0} MB"),
        new("Selecione pelo menos uma categoria.", "Selecione pelo menos uma categoria.", "Select at least one category.", "Select at least one category.", "Selecciona al menos una categoría.", "Selecciona al menos una categoría.", "少なくとも1つのカテゴリを選択してください。"),
        new("Em execução agora: {0}", "Em execução agora: {0}", "Currently running: {0}", "Currently running: {0}", "En ejecución ahora: {0}", "En ejecución ahora: {0}", "現在実行中: {0}"),
        new("O jogo precisa manter pelo menos um perfil de inicialização.", "O jogo precisa manter pelo menos um perfil de inicialização.", "The game must keep at least one launch profile.", "The game must keep at least one launch profile.", "El juego debe conservar al menos un perfil de inicio.", "El juego debe conservar al menos un perfil de inicio.", "ゲームには少なくとも1つの起動プロファイルが必要です。"),
        new("Informe o nome do jogo.", "Introduza o nome do jogo.", "Enter the game name.", "Enter the game name.", "Ingresa el nombre del juego.", "Introduce el nombre del juego.", "ゲーム名を入力してください。"),
        new("TODOS OS GÊNEROS", "TODOS OS GÉNEROS", "ALL GENRES", "ALL GENRES", "TODOS LOS GÉNEROS", "TODOS LOS GÉNEROS", "すべてのジャンル"),
    ];

    private static readonly IReadOnlyDictionary<string, string> JapaneseTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Configurações"] = "設定",
            ["CONFIGURAÇÕES"] = "設定",
            ["Biblioteca"] = "ライブラリ",
            ["Controle parental"] = "ペアレンタルコントロール",
            ["Faixa etária permitida na biblioteca"] = "ライブラリで許可する年齢区分",
            ["PADRÃO — MOSTRAR TODOS"] = "標準 — すべて表示",
            ["IDADE DE 3 A 9 ANOS"] = "3～9歳",
            ["IDADE DE 10 A 13 ANOS"] = "10～13歳",
            ["IDADE DE 14 A 17 ANOS"] = "14～17歳",
            ["ACIMA DE 18 ANOS"] = "18歳以上",
            ["Idioma"] = "言語",
            ["Cor da linha neon"] = "ネオンラインの色",
            ["Atualizar biblioteca"] = "ライブラリを更新",
            ["Atualizações"] = "アップデート",
            ["Atualização do LUDARYX"] = "LUDARYX アップデート",
            ["Baixando atualização"] = "アップデートをダウンロード中",
            ["Preparando download..."] = "ダウンロードを準備中...",
            ["Cancelando..."] = "キャンセル中...",
            ["CANCELAR"] = "キャンセル",
            ["SALVAR"] = "保存",
            ["CONFIRMAR"] = "確認",
            ["CRIAR PIN"] = "PINを作成",
            ["DESBLOQUEAR"] = "ロック解除",
            ["Alterar PIN"] = "PINを変更",
            ["Alterar PIN do controle parental"] = "ペアレンタルコントロールのPINを変更",
            ["PIN atual"] = "現在のPIN",
            ["Confirmar PIN atual"] = "現在のPINを確認",
            ["Criar novo PIN"] = "新しいPINを作成",
            ["Novo PIN"] = "新しいPIN",
            ["Confirmar novo PIN"] = "新しいPINを確認",
            ["SALVAR PIN"] = "PINを保存",
            ["PIN incorreto."] = "PINが正しくありません。",
            ["O PIN deve conter exatamente 4 números."] = "PINは4桁の数字で入力してください。",
            ["Os PINs não são iguais."] = "PINが一致しません。",
            ["PIN alterado com sucesso."] = "PINを変更しました。",
            ["Não informado"] = "情報なし",
            ["Classificação indicativa"] = "年齢レーティング",
            ["Descrição"] = "説明",
            ["Última execução"] = "最終プレイ",
            ["JOGAR"] = "プレイ",
            ["DETALHES / PERSONALIZAR"] = "詳細 / カスタマイズ",
            ["FAVORITO"] = "お気に入り",
            ["OCULTAR"] = "非表示",
            ["DESOCULTAR"] = "再表示",
            ["PESQUISAR"] = "検索",
            ["TODOS"] = "すべて",
            ["FAVORITOS"] = "お気に入り",
            ["RECENTES"] = "最近",
            ["OCULTOS"] = "非表示",
            ["DUPLICADOS"] = "重複",
            ["NOME"] = "名前",
            ["MAIS RECENTES"] = "最近プレイ",
            ["MAIS JOGADOS"] = "プレイ回数",
            ["TEMPO JOGADO"] = "プレイ時間",
            ["PLATAFORMA"] = "プラットフォーム",
            ["DATA DE LANÇAMENTO"] = "発売日",
            ["Ordem padrão"] = "標準順",
            ["Ordem crescente"] = "昇順",
            ["Ordem decrescente"] = "降順",
            ["Adicionar jogo"] = "ゲームを追加",
            ["Jogo Manual"] = "手動ゲーム",
            ["Estatísticas"] = "統計",
            ["Sobre / Créditos"] = "情報 / クレジット",
            ["SOBRE / CRÉDITOS"] = "情報 / クレジット",
            ["Buscar nas configurações"] = "設定を検索",
            ["Abrir LUDARYX"] = "LUDARYXを開く",
            ["Encerrar"] = "終了",
            ["Ação"] = "アクション",
            ["Aventura"] = "アドベンチャー",
            ["Casual"] = "カジュアル",
            ["Indie"] = "インディー",
            ["RPG"] = "RPG",
            ["Estratégia"] = "ストラテジー",
            ["Simulação"] = "シミュレーション",
            ["Esportes"] = "スポーツ",
            ["Corrida"] = "レース",
            ["Quebra-cabeça"] = "パズル",
            ["Plataforma"] = "プラットフォーマー",
            ["Tiro"] = "シューティング",
            ["Luta"] = "格闘",
            ["Terror"] = "ホラー",
            ["Sobrevivência"] = "サバイバル",
            ["Furtividade"] = "ステルス",
            ["Mundo aberto"] = "オープンワールド",
            ["Sobre"] = "概要",
            ["Desenvolvimento com auxílio de IA"] = "AI支援による開発",
            ["O LUDARYX foi desenvolvido com o auxílio de ferramentas de inteligência artificial, incluindo o ChatGPT, em tarefas como geração e revisão de código, correção de erros, documentação, refinamento da interface e organização do projeto。"] = "LUDARYXはChatGPTを含む人工知能ツールの支援を受け、コード生成・レビュー、バグ修正、ドキュメント作成、UI改善、プロジェクト整理などに活用して開発されています。",
            ["Fontes de dados"] = "データソース",
            ["Licenças e atribuições"] = "ライセンスと帰属",
            ["Privacidade"] = "プライバシー",
            ["Diagnóstico"] = "診断",
            ["ABRIR PASTA DE LOGS"] = "ログフォルダーを開く",
            ["COPIAR DIAGNÓSTICO"] = "診断情報をコピー",
            ["VERIFICAR ATUALIZAÇÕES"] = "アップデートを確認",
            ["Arquivos incluídos"] = "同梱ファイル",
            ["FECHAR"] = "閉じる",
            ["ADICIONAR JOGO MANUALMENTE"] = "ゲームを手動追加",
            ["Nome"] = "名前",
            ["Perfil de inicialização"] = "起動プロファイル",
            ["NOVO PERFIL"] = "新規プロファイル",
            ["EXCLUIR PERFIL"] = "プロファイルを削除",
            ["Nome do perfil"] = "プロファイル名",
            ["Executável (.exe)"] = "実行ファイル (.exe)",
            ["PROCURAR"] = "参照",
            ["Executar como administrador"] = "管理者として実行",
            ["ADICIONAR"] = "追加",
            ["EXPORTAR BACKUP"] = "バックアップをエクスポート",
            ["Escolha quais categorias deseja incluir."] = "含めるカテゴリを選択してください。",
            ["Configurações, favoritos, ocultos, estatísticas e jogos manuais"] = "設定、お気に入り、非表示項目、統計、手動ゲーム",
            ["Artes personalizadas"] = "カスタムアート",
            ["Cache de metadados (sem cache automático de capas)"] = "メタデータキャッシュ（自動アートキャッシュを除く）",
            ["CONTINUAR"] = "続行",
            ["Detalhes do jogo"] = "ゲーム詳細",
            ["CAPA VERTICAL"] = "縦カバー",
            ["CAPA HORIZONTAL"] = "横カバー",
            ["☆ FAVORITO"] = "☆ お気に入り",
            ["EXCLUIR DA BIBLIOTECA"] = "ライブラリから削除",
            ["EDITAR INICIALIZAÇÃO"] = "起動設定を編集",
            ["DUPLICAR ENTRADA"] = "エントリを複製",
            ["ABRIR PASTA DO JOGO"] = "ゲームフォルダーを開く",
            ["ATUALIZAR METADADOS"] = "メタデータを更新",
            ["TORNAR ESTA VERSÃO PRINCIPAL"] = "このバージョンを優先",
            ["Gêneros"] = "ジャンル",
            ["Desenvolvedor / Publicadora"] = "開発元 / パブリッシャー",
            ["Lançamento"] = "発売",
            ["Uso local"] = "ローカル使用状況",
            ["Instalação"] = "インストール",
            ["Classificação manual"] = "手動レーティング",
            ["Gêneros separados por vírgula"] = "ジャンル（カンマ区切り）",
            ["Desenvolvedor"] = "開発元",
            ["Publicadora"] = "パブリッシャー",
            ["ESCOLHER CAPA VERTICAL"] = "縦カバーを選択",
            ["RESTAURAR VERTICAL"] = "縦カバーを元に戻す",
            ["ESCOLHER ARTE HORIZONTAL"] = "横アートを選択",
            ["RESTAURAR HORIZONTAL"] = "横アートを元に戻す",
            ["BUSCAR CAPA VERTICAL"] = "縦カバーを検索",
            ["BUSCAR ARTE HORIZONTAL"] = "横アートを検索",
            ["CORRIGIR JOGO DO STEAMGRIDDB"] = "SteamGridDBのゲームを修正",
            ["SALVAR CLASSIFICAÇÃO MANUAL"] = "手動メタデータを保存",
            ["Buscar jogos, gêneros, plataformas, desenvolvedoras e publicadoras"] = "ゲーム、ジャンル、プラットフォーム、開発元、パブリッシャーを検索",
            ["CARREGANDO BIBLIOTECA..."] = "ライブラリを更新中...",
            ["Tela cheia"] = "全画面",
            ["Minimizar"] = "最小化",
            ["Maximizar"] = "最大化",
            ["Fechar"] = "閉じる",
            ["Filtrar biblioteca"] = "ライブラリを絞り込む",
            ["Filtrar por plataforma"] = "プラットフォームで絞り込む",
            ["TODAS PLATAFORMAS"] = "すべてのプラットフォーム",
            ["MANUAIS"] = "手動",
            ["Filtrar por gênero"] = "ジャンルで絞り込む",
            ["Ordenar"] = "並び替え",
            ["ATUALIZAR FILTRO"] = "絞り込みを更新",
            ["LIMPAR"] = "クリア",
            ["+ JOGO MANUAL"] = "+ 手動ゲーム",
            ["Jogos por linha"] = "1行のゲーム数",
            ["Formato da capa: Vertical / Horizontal"] = "カバー形式: 縦 / 横",
            ["Tema: Claro / Escuro"] = "テーマ: ライト / ダーク",
            ["Pesquisar jogos"] = "ゲームを検索",
            ["Sair da tela cheia"] = "全画面を終了",
            ["FILTROS"] = "フィルター",
            ["Clique esquerdo: jogar • clique direito: personalizar"] = "左クリック: プレイ • 右クリック: カスタマイズ",
            ["VOLTAR AO TOPO"] = "先頭へ戻る",
            ["Nenhum jogo encontrado"] = "ゲームが見つかりません",
            ["Nenhum item corresponde aos filtros atuais."] = "現在のフィルターに一致する項目はありません。",
            ["LIMPAR FILTROS"] = "フィルターをクリア",
            ["Controle: procurando..."] = "コントローラーを検索中...",
            ["Preparando inicialização..."] = "起動を準備中...",
            ["PROGRESSO"] = "進行状況",
            ["O LUDARYX manterá esta tela aberta até detectar o processo do jogo."] = "ゲームプロセスを検出するまで、この画面を表示し続けます。",
            ["INTERROMPER"] = "中止",
            ["PIN do controle parental"] = "ペアレンタルコントロールPIN",
            ["Confirmar PIN"] = "PINを確認",
            ["Controle a biblioteca, integrações, backup e itens locais."] = "ライブラリ、連携、バックアップ、ローカル項目を管理します。",
            ["Mostrar itens ocultos na biblioteca"] = "ライブラリで非表示項目を表示",
            ["Ocultar painel lateral e hero"] = "サイドパネルとヒーローを非表示",
            ["Jogos sem classificação utilizável ficam ocultos quando um limite de idade está ativo."] = "年齢制限が有効な場合、利用可能なレーティングがないゲームは非表示になります。",
            ["Execução"] = "起動",
            ["Iniciar clientes automaticamente"] = "クライアントを自動起動",
            ["Manter launcher aberto"] = "ランチャーを開いたままにする",
            ["Iniciar LUDARYX com o Windows"] = "Windows起動時にLUDARYXを開始",
            ["Abrir em tela cheia ao iniciar"] = "起動時に全画面表示",
            ["Procurar atualizações ao iniciar"] = "起動時にアップデートを確認",
            ["VERIFICAR AGORA"] = "今すぐ確認",
            ["Duplicatas"] = "重複",
            ["Mostrar somente a versão principal de duplicatas"] = "重複は優先バージョンのみ表示",
            ["Prioridade automática por plataforma (separe por vírgulas):"] = "プラットフォームの自動優先順位（カンマ区切り）:",
            ["Cache e backup"] = "キャッシュとバックアップ",
            ["Limpar cache automaticamente quando atingir o limite"] = "上限到達時にキャッシュを自動削除",
            ["LIMPAR CACHE AGORA"] = "今すぐキャッシュを削除",
            ["IMPORTAR BACKUP"] = "バックアップをインポート",
            ["ABRIR PASTA DE DADOS"] = "データフォルダーを開く",
            ["Atalhos de teclado"] = "キーボードショートカット",
            ["Status das integrações"] = "連携状態",
            ["ATUALIZAR STATUS"] = "状態を更新",
            ["ABRIR CLIENTE"] = "クライアントを開く",
            ["Gerenciar itens"] = "項目を管理",
            ["Mostrar"] = "表示",
            ["RESTAURAR"] = "復元",
            ["ESTATÍSTICAS LOCAIS"] = "ローカル統計",
            ["Mais jogados"] = "よくプレイするゲーム",
            ["Uso por plataforma"] = "プラットフォーム別使用状況",
            ["Escolher arte do SteamGridDB"] = "SteamGridDBアートを選択",
            ["ESCOLHER ARTE"] = "アートを選択",
            ["USAR ESTA ARTE"] = "このアートを使用",
            ["USAR SELECIONADA"] = "選択したものを使用",
            ["Escolher jogo no SteamGridDB"] = "SteamGridDBのゲームを選択",
            ["ESCOLHER JOGO DO STEAMGRIDDB"] = "STEAMGRIDDBのゲームを選択",
            ["VERIFICADO"] = "確認済み",
            ["USAR SELECIONADO"] = "選択したゲームを使用",
            ["Versão"] = "バージョン",
            ["Não classificado"] = "未分類",
            ["Nunca jogado"] = "未プレイ",
            ["Jogando"] = "プレイ中",
            ["Detectado"] = "検出済み",
            ["Não encontrado"] = "未検出",
            ["API não configurada"] = "API未設定",
            ["API configurada"] = "API設定済み",
            ["Disponível"] = "利用可能",
            ["Em execução"] = "実行中",
            ["Padrão"] = "標準",
            ["Perfil"] = "プロファイル",
            ["Editar inicialização"] = "起動設定を編集",
            ["EDITAR CONFIGURAÇÕES DE INICIALIZAÇÃO"] = "起動設定を編集",
            ["RESTAURAR BACKUP"] = "バックアップを復元",
            ["Restaurar backup"] = "バックアップを復元",
            ["Escolha quais categorias deseja restaurar deste backup."] = "このバックアップから復元するカテゴリを選択してください。",
            ["Sem descrição."] = "説明なし。",
            ["TODOS OS GÊNEROS"] = "すべてのジャンル",
            ["EM EXECUÇÃO"] = "実行中",
            ["★ FAVORITO"] = "★ お気に入り",
            ["Aguardando o jogo..."] = "ゲームを待機中...",
            ["Jogo iniciado"] = "ゲームを開始しました",
            ["Inicialização interrompida"] = "起動を中止しました",
            ["Interrompendo..."] = "中止しています...",
            ["Se o jogo não abrir e o Windows Security mostrar uma notificação, permita o aplicativo e tente novamente."] = "ゲームが開かずWindows セキュリティの通知が表示された場合は、アプリを許可してもう一度お試しください。",

        };

    private static readonly Translation[] Translations =
    [
        new("Configurações", "Definições", "Settings", "Settings", "Configuración", "Configuración"),
        new("CONFIGURAÇÕES", "DEFINIÇÕES", "SETTINGS", "SETTINGS", "CONFIGURACIÓN", "CONFIGURACIÓN"),
        new("Controle a biblioteca, capas, integrações e itens ocultos.", "Controle a biblioteca, capas, integrações e itens ocultos.", "Manage the library, artwork, integrations, and hidden items.", "Manage the library, artwork, integrations, and hidden items.", "Administra la biblioteca, carátulas, integraciones y elementos ocultos.", "Administra la biblioteca, carátulas, integraciones y elementos ocultos."),
        new("Biblioteca", "Biblioteca", "Library", "Library", "Biblioteca", "Biblioteca"),
        new("Mostrar Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store", "Show Xbox / Microsoft Store", "Show Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store"),
        new("Mostrar itens ocultos na biblioteca", "Mostrar itens ocultos na biblioteca", "Show hidden items in library", "Show hidden items in library", "Mostrar elementos ocultos en la biblioteca", "Mostrar elementos ocultos en la biblioteca"),
        new("Ocultar painel lateral e hero", "Ocultar painel lateral e hero", "Hide side panel and hero", "Hide side panel and hero", "Ocultar panel lateral y hero", "Ocultar panel lateral y hero"),
        new("Jogos por linha", "Jogos por linha", "Games per row", "Games per row", "Juegos por fila", "Juegos por fila"),
        new("Formato da capa: Vertical / Horizontal", "Formato da capa: Vertical / Horizontal", "Cover format: Vertical / Horizontal", "Cover format: Vertical / Horizontal", "Formato de portada: Vertical / Horizontal", "Formato de portada: Vertical / Horizontal"),
        new("Tema: Claro / Escuro", "Tema: Claro / Escuro", "Theme: Light / Dark", "Theme: Light / Dark", "Tema: Claro / Oscuro", "Tema: Claro / Oscuro"),
        new("Cor da linha neon", "Cor da linha néon", "Neon line color", "Neon line colour", "Color de la línea neón", "Color de la línea neón"),
        new("Controle a biblioteca, integrações, backup e itens locais.", "Controle a biblioteca, integrações, cópias de segurança e itens locais.", "Manage the library, integrations, backups, and local items.", "Manage the library, integrations, backups, and local items.", "Administra la biblioteca, integraciones, respaldos y elementos locales.", "Administra la biblioteca, integraciones, copias de seguridad y elementos locales."),
        new("Controle parental", "Controlo parental", "Parental controls", "Parental controls", "Control parental", "Control parental"),
        new("Faixa etária permitida na biblioteca", "Faixa etária permitida na biblioteca", "Allowed age range in the library", "Allowed age range in the library", "Rango de edad permitido en la biblioteca", "Rango de edad permitido en la biblioteca"),
        new("PADRÃO — MOSTRAR TODOS", "PADRÃO — MOSTRAR TODOS", "DEFAULT — SHOW ALL", "DEFAULT — SHOW ALL", "PREDETERMINADO — MOSTRAR TODOS", "PREDETERMINADO — MOSTRAR TODOS"),
        new("IDADE DE 3 A 9 ANOS", "IDADE DOS 3 AOS 9 ANOS", "AGE 3 TO 9", "AGE 3 TO 9", "EDAD DE 3 A 9 AÑOS", "EDAD DE 3 A 9 AÑOS"),
        new("IDADE DE 10 A 13 ANOS", "IDADE DOS 10 AOS 13 ANOS", "AGE 10 TO 13", "AGE 10 TO 13", "EDAD DE 10 A 13 AÑOS", "EDAD DE 10 A 13 AÑOS"),
        new("IDADE DE 14 A 17 ANOS", "IDADE DOS 14 AOS 17 ANOS", "AGE 14 TO 17", "AGE 14 TO 17", "EDAD DE 14 A 17 AÑOS", "EDAD DE 14 A 17 AÑOS"),
        new("ACIMA DE 18 ANOS", "ACIMA DOS 18 ANOS", "OVER 18", "OVER 18", "MAYORES DE 18 AÑOS", "MAYORES DE 18 AÑOS"),
        new("Jogos sem classificação utilizável ficam ocultos quando um limite de idade está ativo.", "Jogos sem classificação utilizável ficam ocultos quando um limite de idade está ativo.", "Games without a usable age rating are hidden when an age limit is active.", "Games without a usable age rating are hidden when an age limit is active.", "Los juegos sin una clasificación de edad utilizable se ocultan cuando hay un límite activo.", "Los juegos sin una clasificación de edad utilizable se ocultan cuando hay un límite activo."),
        new("PIN do controle parental", "PIN do controlo parental", "Parental control PIN", "Parental control PIN", "PIN de control parental", "PIN de control parental"),
        new("Criar PIN do controle parental", "Criar PIN do controlo parental", "Create parental control PIN", "Create parental control PIN", "Crear PIN de control parental", "Crear PIN de control parental"),
        new("Desbloquear controle parental", "Desbloquear controlo parental", "Unlock parental controls", "Unlock parental controls", "Desbloquear control parental", "Desbloquear control parental"),
        new("Digite o PIN de 4 números para alterar a faixa etária durante esta sessão.", "Introduza o PIN de 4 números para alterar a faixa etária durante esta sessão.", "Enter the 4-digit PIN to change the age range during this session.", "Enter the 4-digit PIN to change the age range during this session.", "Ingresa el PIN de 4 dígitos para cambiar el rango de edad durante esta sesión.", "Introduce el PIN de 4 dígitos para cambiar el rango de edad durante esta sesión."),
        new("Crie um PIN de exatamente 4 números. Ele será solicitado novamente após encerrar e abrir o LUDARYX.", "Crie um PIN de exatamente 4 números. Será solicitado novamente após encerrar e abrir o LUDARYX.", "Create a PIN of exactly 4 digits. It will be required again after closing and reopening LUDARYX.", "Create a PIN of exactly 4 digits. It will be required again after closing and reopening LUDARYX.", "Crea un PIN de exactamente 4 dígitos. Se volverá a solicitar después de cerrar y abrir LUDARYX.", "Crea un PIN de exactamente 4 dígitos. Se volverá a solicitar después de cerrar y abrir LUDARYX."),
        new("Confirmar PIN", "Confirmar PIN", "Confirm PIN", "Confirm PIN", "Confirmar PIN", "Confirmar PIN"),
        new("O PIN deve conter exatamente 4 números.", "O PIN deve conter exatamente 4 números.", "The PIN must contain exactly 4 digits.", "The PIN must contain exactly 4 digits.", "El PIN debe contener exactamente 4 dígitos.", "El PIN debe contener exactamente 4 dígitos."),
        new("Os PINs não são iguais.", "Os PINs não são iguais.", "The PINs do not match.", "The PINs do not match.", "Los PIN no coinciden.", "Los PIN no coinciden."),
        new("PIN incorreto.", "PIN incorreto.", "Incorrect PIN.", "Incorrect PIN.", "PIN incorrecto.", "PIN incorrecto."),
        new("CRIAR PIN", "CRIAR PIN", "CREATE PIN", "CREATE PIN", "CREAR PIN", "CREAR PIN"),
        new("DESBLOQUEAR", "DESBLOQUEAR", "UNLOCK", "UNLOCK", "DESBLOQUEAR", "DESBLOQUEAR"),
        new("Alterar PIN", "Alterar PIN", "Change PIN", "Change PIN", "Cambiar PIN", "Cambiar PIN"),
        new("Alterar PIN do controle parental", "Alterar PIN do controlo parental", "Change parental control PIN", "Change parental control PIN", "Cambiar PIN de control parental", "Cambiar PIN de control parental"),
        new("Digite o PIN atual e confirme-o novamente antes de escolher um novo PIN.", "Introduza o PIN atual e confirme-o novamente antes de escolher um novo PIN.", "Enter the current PIN and confirm it again before choosing a new PIN.", "Enter the current PIN and confirm it again before choosing a new PIN.", "Ingresa el PIN actual y confírmalo nuevamente antes de elegir un PIN nuevo.", "Introduce el PIN actual y confírmalo de nuevo antes de elegir un PIN nuevo."),
        new("PIN atual", "PIN atual", "Current PIN", "Current PIN", "PIN actual", "PIN actual"),
        new("Confirmar PIN atual", "Confirmar PIN atual", "Confirm current PIN", "Confirm current PIN", "Confirmar PIN actual", "Confirmar PIN actual"),
        new("Criar novo PIN", "Criar novo PIN", "Create new PIN", "Create new PIN", "Crear PIN nuevo", "Crear PIN nuevo"),
        new("Novo PIN", "Novo PIN", "New PIN", "New PIN", "PIN nuevo", "PIN nuevo"),
        new("Confirmar novo PIN", "Confirmar novo PIN", "Confirm new PIN", "Confirm new PIN", "Confirmar PIN nuevo", "Confirmar PIN nuevo"),
        new("Digite o novo PIN de 4 números e confirme-o novamente.", "Introduza o novo PIN de 4 números e confirme-o novamente.", "Enter the new 4-digit PIN and confirm it again.", "Enter the new 4-digit PIN and confirm it again.", "Ingresa el PIN nuevo de 4 dígitos y confírmalo nuevamente.", "Introduce el PIN nuevo de 4 dígitos y confírmalo de nuevo."),
        new("SALVAR PIN", "GUARDAR PIN", "SAVE PIN", "SAVE PIN", "GUARDAR PIN", "GUARDAR PIN"),
        new("PIN alterado com sucesso.", "PIN alterado com sucesso.", "PIN changed successfully.", "PIN changed successfully.", "PIN cambiado correctamente.", "PIN cambiado correctamente."),
        new("Execução", "Execução", "Launch", "Launch", "Ejecución", "Ejecución"),
        new("Iniciar clientes automaticamente", "Iniciar clientes automaticamente", "Start clients automatically", "Start clients automatically", "Iniciar clientes automáticamente", "Iniciar clientes automáticamente"),
        new("Manter launcher aberto", "Manter launcher aberto", "Keep launcher open", "Keep launcher open", "Mantener launcher abierto", "Mantener launcher abierto"),
        new("Iniciar LUDARYX com o Windows", "Iniciar LUDARYX com o Windows", "Start LUDARYX with Windows", "Start LUDARYX with Windows", "Iniciar LUDARYX con Windows", "Iniciar LUDARYX con Windows"),
        new("Abrir em tela cheia ao iniciar", "Abrir em ecrã inteiro ao iniciar", "Start in fullscreen", "Start in fullscreen", "Abrir en pantalla completa al iniciar", "Abrir en pantalla completa al iniciar"),
        new("Capas", "Capas", "Artwork", "Artwork", "Carátulas", "Carátulas"),
        new("Auto", "Automático", "Auto", "Auto", "Auto", "Auto"),
        new("Horizontal", "Horizontal", "Horizontal", "Horizontal", "Horizontal", "Horizontal"),
        new("Vertical", "Vertical", "Vertical", "Vertical", "Vertical", "Vertical"),
        new("Linha neon", "Linha néon", "Neon line", "Neon line", "Línea neón", "Línea neón"),
        new("Vermelha", "Vermelha", "Red", "Red", "Roja", "Roja"),
        new("Azul claro", "Azul-claro", "Light blue", "Light blue", "Azul claro", "Azul claro"),
        new("Tema", "Tema", "Theme", "Theme", "Tema", "Tema"),
        new("Escuro", "Escuro", "Dark", "Dark", "Oscuro", "Oscuro"),
        new("Claro", "Claro", "Light", "Light", "Claro", "Claro"),
        new("Idioma", "Idioma", "Language", "Language", "Idioma", "Idioma"),
        new("Use a API Key do SteamGridDB para buscar automaticamente capas verticais e horizontais, inclusive de jogos de outros launchers.", "Use a chave API do SteamGridDB para procurar automaticamente capas verticais e horizontais, incluindo jogos de outros launchers.", "Use your SteamGridDB API key to automatically fetch vertical and horizontal covers, including games from other launchers.", "Use your SteamGridDB API key to automatically fetch vertical and horizontal covers, including games from other launchers.", "Usa tu clave API de SteamGridDB para buscar automáticamente carátulas verticales y horizontales, incluso para juegos de otros launchers.", "Usa tu clave API de SteamGridDB para buscar automáticamente carátulas verticales y horizontales, incluso para juegos de otros launchers."),
        new("Atualizações", "Atualizações", "Updates", "Updates", "Actualizaciones", "Actualizaciones"),
        new("Atualização do LUDARYX", "Atualização do LUDARYX", "LUDARYX Update", "LUDARYX Update", "Actualización de LUDARYX", "Actualización de LUDARYX"),
        new("Baixando atualização", "A transferir atualização", "Downloading update", "Downloading update", "Descargando actualización", "Descargando actualización"),
        new("Preparando download...", "A preparar transferência...", "Preparing download...", "Preparing download...", "Preparando descarga...", "Preparando descarga..."),
        new("Cancelando...", "A cancelar...", "Cancelling...", "Cancelling...", "Cancelando...", "Cancelando..."),
        new("Versão", "Versão", "Version", "Version", "Versión", "Versión"),
        new("Verifique novas versões publicadas no GitHub e baixe o instalador mais recente quando disponível.", "Verifique novas versões publicadas no GitHub e transfira o instalador mais recente quando disponível.", "Check for new versions published on GitHub and download the latest installer when available.", "Check for new versions published on GitHub and download the latest installer when available.", "Consulta nuevas versiones publicadas en GitHub y descarga el instalador más reciente cuando esté disponible.", "Consulta nuevas versiones publicadas en GitHub y descarga el instalador más reciente cuando esté disponible."),
        new("Procurar atualizações ao iniciar", "Procurar atualizações ao iniciar", "Check for updates on startup", "Check for updates on startup", "Buscar actualizaciones al iniciar", "Buscar actualizaciones al iniciar"),
        new("Detectado", "Detetado", "Detected", "Detected", "Detectado", "Detectado"),
        new("Não encontrado", "Não encontrado", "Not found", "Not found", "No encontrado", "No encontrado"),
        new("API configurada", "API configurada", "API configured", "API configured", "API configurada", "API configurada"),
        new("API não configurada", "API não configurada", "API not configured", "API not configured", "API no configurada", "API no configurada"),
        new("Disponível", "Disponível", "Available", "Available", "Disponible", "Disponible"),
        new("Editar inicialização", "Editar arranque", "Edit launch settings", "Edit launch settings", "Editar inicio", "Editar inicio"),
        new("VERIFICAR AGORA", "VERIFICAR AGORA", "CHECK NOW", "CHECK NOW", "COMPROBAR AHORA", "COMPROBAR AHORA"),
        new("VOLTAR AO TOPO", "VOLTAR AO TOPO", "BACK TO TOP", "BACK TO TOP", "VOLVER ARRIBA", "VOLVER ARRIBA"),
        new("Verificando atualizações no GitHub...", "A verificar atualizações no GitHub...", "Checking for updates on GitHub...", "Checking for updates on GitHub...", "Buscando actualizaciones en GitHub...", "Buscando actualizaciones en GitHub..."),
        new("Duplicatas", "Duplicados", "Duplicates", "Duplicates", "Duplicados", "Duplicados"),
        new("Ocultar automaticamente versões duplicadas secundárias", "Ocultar automaticamente versões duplicadas secundárias", "Automatically hide secondary duplicate versions", "Automatically hide secondary duplicate versions", "Ocultar automáticamente versiones duplicadas secundarias", "Ocultar automáticamente versiones duplicadas secundarias"),
        new("Mostrar somente a versão principal de duplicatas", "Mostrar apenas a versão principal de duplicados", "Show only the primary version of duplicates", "Show only the primary version of duplicates", "Mostrar solo la versión principal de duplicados", "Mostrar solo la versión principal de duplicados"),
        new("Prioridade automática por plataforma (separe por vírgulas):", "Prioridade automática por plataforma (separe por vírgulas):", "Automatic platform priority (separate with commas):", "Automatic platform priority (separate with commas):", "Prioridad automática por plataforma (separa con comas):", "Prioridad automática por plataforma (separa con comas):"),
        new("Atalhos de teclado", "Atalhos de teclado", "Keyboard shortcuts", "Keyboard shortcuts", "Atajos de teclado", "Atajos de teclado"),
        new("Pesquisar:", "Pesquisar:", "Search:", "Search:", "Buscar:", "Buscar:"),
        new("Tela cheia:", "Ecrã inteiro:", "Fullscreen:", "Fullscreen:", "Pantalla completa:", "Pantalla completa:"),
        new("Atualizar biblioteca:", "Atualizar biblioteca:", "Refresh library:", "Refresh library:", "Actualizar biblioteca:", "Actualizar biblioteca:"),
        new("Configurações:", "Definições:", "Settings:", "Settings:", "Configuración:", "Configuración:"),
        new("ATUALIZAR STATUS", "ATUALIZAR ESTADO", "REFRESH STATUS", "REFRESH STATUS", "ACTUALIZAR ESTADO", "ACTUALIZAR ESTADO"),
        new("Em execução", "Em execução", "Running", "Running", "En ejecución", "En ejecución"),
        new("EXPORTAR BACKUP", "EXPORTAR CÓPIA", "EXPORT BACKUP", "EXPORT BACKUP", "EXPORTAR COPIA", "EXPORTAR COPIA"),
        new("Escolha quais categorias deseja incluir.", "Escolha as categorias que pretende incluir.", "Choose which categories to include.", "Choose which categories to include.", "Elige qué categorías incluir.", "Elige qué categorías incluir."),
        new("Configurações, favoritos, ocultos, estatísticas e jogos manuais", "Definições, favoritos, ocultos, estatísticas e jogos manuais", "Settings, favorites, hidden items, statistics, and manual games", "Settings, favourites, hidden items, statistics, and manual games", "Configuración, favoritos, ocultos, estadísticas y juegos manuales", "Configuración, favoritos, ocultos, estadísticas y juegos manuales"),
        new("Artes personalizadas", "Artes personalizadas", "Custom artwork", "Custom artwork", "Arte personalizado", "Arte personalizado"),
        new("Cache de metadados (sem cache automático de capas)", "Cache de metadados (sem cache automática de capas)", "Metadata cache (without automatic artwork cache)", "Metadata cache (without automatic artwork cache)", "Caché de metadatos (sin caché automática de carátulas)", "Caché de metadatos (sin caché automática de carátulas)"),
        new("CONTINUAR", "CONTINUAR", "CONTINUE", "CONTINUE", "CONTINUAR", "CONTINUAR"),

        new("Escolha a versão principal na tela de detalhes de qualquer jogo marcado como duplicata.", "Escolha a versão principal no ecrã de detalhes de qualquer jogo marcado como duplicado.", "Choose the primary version from the details screen of any game marked as a duplicate.", "Choose the primary version from the details screen of any game marked as a duplicate.", "Elige la versión principal en la pantalla de detalles de cualquier juego marcado como duplicado.", "Elige la versión principal en la pantalla de detalles de cualquier juego marcado como duplicado."),
        new("Cache e backup", "Cache e cópia de segurança", "Cache and backup", "Cache and backup", "Caché y copia de seguridad", "Caché y copia de seguridad"),
        new("Limpar cache automaticamente quando atingir o limite", "Limpar a cache automaticamente quando atingir o limite", "Automatically clean cache when it reaches the limit", "Automatically clean cache when it reaches the limit", "Limpiar automáticamente la caché al alcanzar el límite", "Limpiar automáticamente la caché al alcanzar el límite"),
        new("Limite do cache (MB):", "Limite da cache (MB):", "Cache limit (MB):", "Cache limit (MB):", "Límite de caché (MB):", "Límite de caché (MB):"),
        new("LIMPAR CACHE AGORA", "LIMPAR CACHE AGORA", "CLEAR CACHE NOW", "CLEAR CACHE NOW", "LIMPIAR CACHÉ AHORA", "LIMPIAR CACHÉ AHORA"),
        new("EXPORTAR BACKUP", "EXPORTAR CÓPIA", "EXPORT BACKUP", "EXPORT BACKUP", "EXPORTAR COPIA", "EXPORTAR COPIA"),
        new("IMPORTAR BACKUP", "IMPORTAR CÓPIA", "IMPORT BACKUP", "IMPORT BACKUP", "IMPORTAR COPIA", "IMPORTAR COPIA"),
        new("O backup inclui configurações, favoritos, jogos manuais e artes personalizadas. O cache automático de capas não é incluído porque pode ser recriado.", "A cópia inclui configurações, favoritos, jogos manuais e artes personalizadas. A cache automática de capas não é incluída porque pode ser recriada.", "The backup includes settings, favorites, manual games, and custom artwork. Automatic artwork cache is excluded because it can be rebuilt.", "The backup includes settings, favourites, manual games, and custom artwork. Automatic artwork cache is excluded because it can be rebuilt.", "La copia incluye ajustes, favoritos, juegos manuales y arte personalizado. La caché automática de carátulas no se incluye porque puede volver a generarse.", "La copia incluye ajustes, favoritos, juegos manuales y arte personalizado. La caché automática de carátulas no se incluye porque puede volver a generarse."),
        new("ABRIR PASTA DE DADOS", "ABRIR PASTA DE DADOS", "OPEN DATA FOLDER", "OPEN DATA FOLDER", "ABRIR CARPETA DE DATOS", "ABRIR CARPETA DE DATOS"),
        new("ABRIR CLIENTE", "ABRIR CLIENTE", "OPEN CLIENT", "OPEN CLIENT", "ABRIR CLIENTE", "ABRIR CLIENTE"),
        new("Status das integrações", "Estado das integrações", "Integration status", "Integration status", "Estado de las integraciones", "Estado de las integraciones"),
        new("Mostra quais clientes e integrações foram encontrados neste computador.", "Mostra quais clientes e integrações foram encontrados neste computador.", "Shows which clients and integrations were found on this computer.", "Shows which clients and integrations were found on this computer.", "Muestra qué clientes e integraciones se encontraron en este equipo.", "Muestra qué clientes e integraciones se encontraron en este equipo."),
        new("Gerenciar itens", "Gerir itens", "Manage items", "Manage items", "Administrar elementos", "Administrar elementos"),
        new("Use os controles para ocultar ou restaurar jogos. Excluir da biblioteca pode ser restaurado aqui.", "Use os controlos para ocultar ou restaurar jogos. Os jogos removidos da biblioteca podem ser restaurados aqui.", "Use the controls to hide or restore games. Games removed from the library can be restored here.", "Use the controls to hide or restore games. Games removed from the library can be restored here.", "Usa los controles para ocultar o restaurar juegos. Los juegos eliminados de la biblioteca se pueden restaurar aquí.", "Usa los controles para ocultar o restaurar juegos. Los juegos eliminados de la biblioteca se pueden restaurar aquí."),
        new("FAVORITO", "FAVORITO", "FAVORITE", "FAVOURITE", "FAVORITO", "FAVORITO"),
        new("Mostrar", "Mostrar", "Show", "Show", "Mostrar", "Mostrar"),
        new("RESTAURAR", "RESTAURAR", "RESTORE", "RESTORE", "RESTAURAR", "RESTAURAR"),
        new("DESOCULTAR", "MOSTRAR", "UNHIDE", "UNHIDE", "MOSTRAR", "MOSTRAR"),
        new("CANCELAR", "CANCELAR", "CANCEL", "CANCEL", "CANCELAR", "CANCELAR"),
        new("SALVAR", "GUARDAR", "SAVE", "SAVE", "GUARDAR", "GUARDAR"),

        new("Filtrar biblioteca", "Filtrar biblioteca", "Filter library", "Filter library", "Filtrar biblioteca", "Filtrar biblioteca"),
        new("Filtrar por plataforma", "Filtrar por plataforma", "Filter by platform", "Filter by platform", "Filtrar por plataforma", "Filtrar por plataforma"),
        new("Ordenar", "Ordenar", "Sort", "Sort", "Ordenar", "Ordenar"),
        new("TODAS PLATAFORMAS", "TODAS AS PLATAFORMAS", "ALL PLATFORMS", "ALL PLATFORMS", "TODAS LAS PLATAFORMAS", "TODAS LAS PLATAFORMAS"),
        new("NOME", "NOME", "NAME", "NAME", "NOMBRE", "NOMBRE"),
        new("MAIS JOGADOS", "MAIS JOGADOS", "MOST PLAYED", "MOST PLAYED", "MÁS JUGADOS", "MÁS JUGADOS"),
        new("TEMPO JOGADO", "TEMPO JOGADO", "PLAY TIME", "PLAY TIME", "TIEMPO JUGADO", "TIEMPO JUGADO"),
        new("DATA DE LANÇAMENTO", "DATA DE LANÇAMENTO", "RELEASE DATE", "RELEASE DATE", "FECHA DE LANZAMIENTO", "FECHA DE LANZAMIENTO"),
        new("Ordem padrão", "Ordem padrão", "Default order", "Default order", "Orden predeterminado", "Orden predeterminado"),
        new("Ordem crescente", "Ordem crescente", "Ascending order", "Ascending order", "Orden ascendente", "Orden ascendente"),
        new("Ordem decrescente", "Ordem decrescente", "Descending order", "Descending order", "Orden descendente", "Orden descendente"),
        new("ESTATÍSTICAS", "ESTATÍSTICAS", "STATISTICS", "STATISTICS", "ESTADÍSTICAS", "ESTADÍSTICAS"),
        new("ATUALIZAR FILTRO", "ATUALIZAR FILTRO", "REFRESH FILTERED", "REFRESH FILTERED", "ACTUALIZAR FILTRO", "ACTUALIZAR FILTRO"),
        new("LIMPAR", "LIMPAR", "CLEAR", "CLEAR", "LIMPIAR", "LIMPIAR"),
        new("+ JOGO MANUAL", "+ JOGO MANUAL", "+ MANUAL GAME", "+ MANUAL GAME", "+ JUEGO MANUAL", "+ JUEGO MANUAL"),
        new("FILTROS", "FILTROS", "FILTERS", "FILTERS", "FILTROS", "FILTROS"),
        new("Nenhum jogo encontrado", "Nenhum jogo encontrado", "No games found", "No games found", "No se encontraron juegos", "No se encontraron juegos"),
        new("Nenhum item corresponde aos filtros atuais.", "Nenhum item corresponde aos filtros atuais.", "No items match the current filters.", "No items match the current filters.", "Ningún elemento coincide con los filtros actuales.", "Ningún elemento coincide con los filtros actuales."),
        new("LIMPAR FILTROS", "LIMPAR FILTROS", "CLEAR FILTERS", "CLEAR FILTERS", "LIMPIAR FILTROS", "LIMPIAR FILTROS"),
        new("DETALHES / PERSONALIZAR", "DETALHES / PERSONALIZAR", "DETAILS / CUSTOMIZE", "DETAILS / CUSTOMISE", "DETALLES / PERSONALIZAR", "DETALLES / PERSONALIZAR"),
        new("Ações do jogo", "Ações do jogo", "Game actions", "Game actions", "Acciones del juego", "Acciones del juego"),
        new("Sem descrição.", "Sem descrição.", "No description.", "No description.", "Sin descripción.", "Sin descripción."),
        new("EM EXECUÇÃO", "EM EXECUÇÃO", "RUNNING", "RUNNING", "EN EJECUCIÓN", "EN EJECUCIÓN"),
        new("Buscar jogos, gêneros, plataformas, desenvolvedoras e publicadoras", "Pesquisar jogos, géneros, plataformas, programadores e editoras", "Search games, genres, platforms, developers and publishers", "Search games, genres, platforms, developers and publishers", "Buscar juegos, géneros, plataformas, desarrolladores y editores", "Buscar juegos, géneros, plataformas, desarrolladores y editores"),

        new("JOGANDO", "A JOGAR", "PLAYING", "PLAYING", "JUGANDO", "JUGANDO"),
        new("ESTATÍSTICAS LOCAIS", "ESTATÍSTICAS LOCAIS", "LOCAL STATISTICS", "LOCAL STATISTICS", "ESTADÍSTICAS LOCALES", "ESTADÍSTICAS LOCALES"),
        new("Mais jogados", "Mais jogados", "Most played", "Most played", "Más jugados", "Más jugados"),
        new("Uso por plataforma", "Utilização por plataforma", "Usage by platform", "Usage by platform", "Uso por plataforma", "Uso por plataforma"),
        new("Perfil de inicialização", "Perfil de arranque", "Launch profile", "Launch profile", "Perfil de inicio", "Perfil de inicio"),
        new("NOVO PERFIL", "NOVO PERFIL", "NEW PROFILE", "NEW PROFILE", "NUEVO PERFIL", "NUEVO PERFIL"),
        new("EXCLUIR PERFIL", "ELIMINAR PERFIL", "DELETE PROFILE", "DELETE PROFILE", "ELIMINAR PERFIL", "ELIMINAR PERFIL"),
        new("Nome do perfil", "Nome do perfil", "Profile name", "Profile name", "Nombre del perfil", "Nombre del perfil"),
        new("Ícone personalizado (opcional)", "Ícone personalizado (opcional)", "Custom icon (optional)", "Custom icon (optional)", "Icono personalizado (opcional)", "Icono personalizado (opcional)"),
        new("Crie perfis diferentes para iniciar o mesmo jogo com argumentos distintos, por exemplo Normal, Sem mods ou Modo janela. O perfil selecionado será usado ao jogar.", "Crie perfis diferentes para iniciar o mesmo jogo com argumentos distintos, por exemplo Normal, Sem mods ou Modo janela. O perfil selecionado será usado ao jogar.", "Create different profiles to launch the same game with different arguments, for example Normal, No mods, or Windowed mode. The selected profile will be used when playing.", "Create different profiles to launch the same game with different arguments, for example Normal, No mods, or Windowed mode. The selected profile will be used when playing.", "Crea perfiles diferentes para iniciar el mismo juego con argumentos distintos, por ejemplo Normal, Sin mods o Modo ventana. Se usará el perfil seleccionado al jugar.", "Crea perfiles diferentes para iniciar el mismo juego con argumentos distintos, por ejemplo Normal, Sin mods o Modo ventana. Se usará el perfil seleccionado al jugar."),
        new("Gêneros", "Géneros", "Genres", "Genres", "Géneros", "Géneros"),
        new("Classificação indicativa", "Classificação etária", "Age rating", "Age rating", "Clasificación por edades", "Clasificación por edades"),
        new("Uso local", "Utilização local", "Local usage", "Local usage", "Uso local", "Uso local"),
        new("Instalação", "Instalação", "Installation", "Installation", "Instalación", "Instalación"),
        new("ABRIR PASTA DO JOGO", "ABRIR PASTA DO JOGO", "OPEN GAME FOLDER", "OPEN GAME FOLDER", "ABRIR CARPETA DEL JUEGO", "ABRIR CARPETA DEL JUEGO"),
        new("ATUALIZAR METADADOS", "ATUALIZAR METADADOS", "REFRESH METADATA", "REFRESH METADATA", "ACTUALIZAR METADATOS", "ACTUALIZAR METADATOS"),
        new("DUPLICAR ENTRADA", "DUPLICAR ENTRADA", "DUPLICATE ENTRY", "DUPLICATE ENTRY", "DUPLICAR ENTRADA", "DUPLICAR ENTRADA"),
        new("Última execução", "Última execução", "Last played", "Last played", "Última ejecución", "Última ejecución"),
        new("JOGANDO AGORA", "A JOGAR AGORA", "PLAYING NOW", "PLAYING NOW", "JUGANDO AHORA", "JUGANDO AHORA"),
        new("Não informado", "Não informado", "Not available", "Not available", "No disponible", "No disponible"),
        new("A pasta do jogo não foi encontrada.", "A pasta do jogo não foi encontrada.", "The game folder was not found.", "The game folder was not found.", "No se encontró la carpeta del juego.", "No se encontró la carpeta del juego."),
        new("Entrada manual duplicada. Atualize a biblioteca para exibi-la.", "Entrada manual duplicada. Atualize a biblioteca para a apresentar.", "Manual entry duplicated. Refresh the library to display it.", "Manual entry duplicated. Refresh the library to display it.", "Entrada manual duplicada. Actualiza la biblioteca para mostrarla.", "Entrada manual duplicada. Actualiza la biblioteca para mostrarla."),
        new("Pesquisar jogos", "Pesquisar jogos", "Search games", "Search games", "Buscar juegos", "Buscar juegos"),
        new("0 jogos exibidos", "0 jogos apresentados", "0 games shown", "0 games shown", "0 juegos mostrados", "0 juegos mostrados"),
        new("CARREGANDO BIBLIOTECA...", "A CARREGAR BIBLIOTECA...", "LOADING LIBRARY...", "LOADING LIBRARY...", "CARGANDO BIBLIOTECA...", "CARGANDO BIBLIOTECA..."),
        new("ATUALIZANDO BIBLIOTECA...", "A ATUALIZAR BIBLIOTECA...", "UPDATING LIBRARY...", "UPDATING LIBRARY...", "ACTUALIZANDO BIBLIOTECA...", "ACTUALIZANDO BIBLIOTECA..."),
        new("Tela cheia", "Ecrã inteiro", "Fullscreen", "Fullscreen", "Pantalla completa", "Pantalla completa"),
        new("Sair da tela cheia", "Sair do ecrã inteiro", "Exit fullscreen", "Exit fullscreen", "Salir de pantalla completa", "Salir de pantalla completa"),
        new("Minimizar", "Minimizar", "Minimize", "Minimise", "Minimizar", "Minimizar"),
        new("Maximizar", "Maximizar", "Maximize", "Maximise", "Maximizar", "Maximizar"),
        new("Fechar", "Fechar", "Close", "Close", "Cerrar", "Cerrar"),
        new("TODOS", "TODOS", "ALL", "ALL", "TODOS", "TODOS"),
        new("FAVORITOS", "FAVORITOS", "FAVORITES", "FAVOURITES", "FAVORITOS", "FAVORITOS"),
        new("RECENTES", "RECENTES", "RECENT", "RECENT", "RECIENTES", "RECIENTES"),
        new("DUPLICADOS", "DUPLICADOS", "DUPLICATES", "DUPLICATES", "DUPLICADOS", "DUPLICADOS"),
        new("OCULTOS", "OCULTOS", "HIDDEN", "HIDDEN", "OCULTOS", "OCULTOS"),
        new("PLATAFORMA", "PLATAFORMA", "PLATFORM", "PLATFORM", "PLATAFORMA", "PLATAFORMA"),
        new("MANUAIS", "MANUAIS", "MANUAL", "MANUAL", "MANUALES", "MANUALES"),
        new("Filtrar por gênero", "Filtrar por género", "Filter by genre", "Filter by genre", "Filtrar por género", "Filtrar por género"),
        new("ADICIONAR JOGO", "ADICIONAR JOGO", "ADD GAME", "ADD GAME", "AÑADIR JUEGO", "AÑADIR JUEGO"),
        new("ATUALIZAR", "ATUALIZAR", "REFRESH", "REFRESH", "ACTUALIZAR", "ACTUALIZAR"),
        new("Clique esquerdo: jogar • clique direito: personalizar", "Clique esquerdo: jogar • clique direito: personalizar", "Left click: play • right click: customize", "Left click: play • right click: customise", "Clic izquierdo: jugar • clic derecho: personalizar", "Clic izquierdo: jugar • clic derecho: personalizar"),
        new("Controle: procurando...", "Comando: a procurar...", "Controller: searching...", "Controller: searching...", "Control: buscando...", "Mando: buscando..."),
        new("Controle conectado", "Comando ligado", "Controller connected", "Controller connected", "Control conectado", "Mando conectado"),
        new("MODO TV", "MODO TV", "TV MODE", "TV MODE", "MODO TV", "MODO TV"),
        new("Nenhum jogo", "Nenhum jogo", "No game", "No game", "Ningún juego", "Ningún juego"),
        new("Barra de opções", "Barra de opções", "Options bar", "Options bar", "Barra de opciones", "Barra de opciones"),
        new("TODOS OS GÊNEROS", "TODOS OS GÉNEROS", "ALL GENRES", "ALL GENRES", "TODOS LOS GÉNEROS", "TODOS LOS GÉNEROS"),

        // Gêneros de jogos. Os metadados externos normalmente chegam em inglês;
        // manter as traduções aqui evita alterar os valores canônicos usados nos filtros.
        new("Ação", "Ação", "Action", "Action", "Acción", "Acción"),
        new("Ação e aventura", "Ação e aventura", "Action-Adventure", "Action-Adventure", "Acción y aventura", "Acción y aventura"),
        new("Aventura", "Aventura", "Adventure", "Adventure", "Aventura", "Aventura"),
        new("Casual", "Casual", "Casual", "Casual", "Casual", "Casual"),
        new("Indie", "Indie", "Indie", "Indie", "Indie", "Indie"),
        new("RPG", "RPG", "RPG", "RPG", "RPG", "RPG"),
        new("Estratégia", "Estratégia", "Strategy", "Strategy", "Estrategia", "Estrategia"),
        new("Simulação", "Simulação", "Simulation", "Simulation", "Simulación", "Simulación"),
        new("Esportes", "Desporto", "Sports", "Sports", "Deportes", "Deportes"),
        new("Corrida", "Corridas", "Racing", "Racing", "Carreras", "Carreras"),
        new("Quebra-cabeça", "Quebra-cabeças", "Puzzle", "Puzzle", "Puzles", "Puzles"),
        new("Plataforma", "Plataformas", "Platformer", "Platformer", "Plataformas", "Plataformas"),
        new("Tiro", "Tiro", "Shooter", "Shooter", "Disparos", "Disparos"),
        new("FPS", "FPS", "FPS", "FPS", "FPS", "FPS"),
        new("Tiro em terceira pessoa", "Tiro na terceira pessoa", "Third-Person Shooter", "Third-Person Shooter", "Disparos en tercera persona", "Disparos en tercera persona"),
        new("Luta", "Luta", "Fighting", "Fighting", "Lucha", "Lucha"),
        new("Arcade", "Arcade", "Arcade", "Arcade", "Arcade", "Arcade"),
        new("Terror", "Terror", "Horror", "Horror", "Terror", "Terror"),
        new("Sobrevivência", "Sobrevivência", "Survival", "Survival", "Supervivencia", "Supervivencia"),
        new("Furtividade", "Furtividade", "Stealth", "Stealth", "Sigilo", "Sigilo"),
        new("Mundo aberto", "Mundo aberto", "Open World", "Open World", "Mundo abierto", "Mundo abierto"),
        new("Multijogador massivo", "Multijogador massivo", "Massively Multiplayer", "Massively Multiplayer", "Multijugador masivo", "Multijugador masivo"),
        new("MMORPG", "MMORPG", "MMORPG", "MMORPG", "MMORPG", "MMORPG"),
        new("Família", "Família", "Family", "Family", "Familiar", "Familiar"),
        new("Jogo de cartas", "Jogo de cartas", "Card Game", "Card Game", "Juego de cartas", "Juego de cartas"),
        new("Jogo de tabuleiro", "Jogo de tabuleiro", "Board Game", "Board Game", "Juego de mesa", "Juego de mesa"),
        new("Música", "Música", "Music", "Music", "Música", "Música"),
        new("Festa", "Festa", "Party", "Party", "Fiesta", "Fiesta"),
        new("Romance visual", "Romance visual", "Visual Novel", "Visual Novel", "Novela visual", "Novela visual"),
        new("Roguelike", "Roguelike", "Roguelike", "Roguelike", "Roguelike", "Roguelike"),
        new("Roguelite", "Roguelite", "Roguelite", "Roguelite", "Roguelite", "Roguelite"),
        new("Metroidvania", "Metroidvania", "Metroidvania", "Metroidvania", "Metroidvania", "Metroidvania"),
        new("Souls-like", "Souls-like", "Souls-like", "Souls-like", "Souls-like", "Souls-like"),
        new("Hack and Slash", "Hack and Slash", "Hack and Slash", "Hack and Slash", "Hack and Slash", "Hack and Slash"),
        new("Briga de rua", "Briga de rua", "Beat 'em up", "Beat 'em up", "Yo contra el barrio", "Yo contra el barrio"),
        new("Grátis para jogar", "Gratuito", "Free to Play", "Free to Play", "Gratis", "Gratis"),
        new("Gratuito para jogar", "Gratuito para jogar", "Free to Play", "Free to Play", "Gratis", "Gratis"),
        new("Acesso antecipado", "Acesso antecipado", "Early Access", "Early Access", "Acceso anticipado", "Acceso anticipado"),
        new("Apontar e clicar", "Apontar e clicar", "Point-and-click", "Point-and-click", "Apuntar y hacer clic", "Apuntar y hacer clic"),
        new("Simulador", "Simulador", "Simulator", "Simulator", "Simulador", "Simulador"),
        new("Esporte", "Desporto", "Sport", "Sport", "Deporte", "Deporte"),
        new("Estratégia em tempo real (RTS)", "Estratégia em tempo real (RTS)", "Real Time Strategy (RTS)", "Real Time Strategy (RTS)", "Estrategia en tiempo real (RTS)", "Estrategia en tiempo real (RTS)"),
        new("Estratégia por turnos (TBS)", "Estratégia por turnos (TBS)", "Turn-based strategy (TBS)", "Turn-based strategy (TBS)", "Estrategia por turnos (TBS)", "Estrategia por turnos (TBS)"),
        new("Tático", "Tático", "Tactical", "Tactical", "Táctico", "Táctico"),
        new("Perguntas e respostas", "Perguntas e respostas", "Quiz/Trivia", "Quiz/Trivia", "Preguntas y respuestas", "Preguntas y respuestas"),
        new("Pinball", "Pinball", "Pinball", "Pinball", "Pinball", "Pinball"),
        new("Cartas e tabuleiro", "Cartas e tabuleiro", "Card & Board Game", "Card & Board Game", "Cartas y juegos de mesa", "Cartas y juegos de mesa"),
        new("MOBA", "MOBA", "MOBA", "MOBA", "MOBA", "MOBA"),
        new("Histórico", "Histórico", "Historical", "Historical", "Histórico", "Histórico"),
        new("Combate", "Combate", "Combat", "Combat", "Combate", "Combate"),
        new("Fantasia", "Fantasia", "Fantasy", "Fantasy", "Fantasía", "Fantasía"),
        new("Abrir LUDARYX", "Abrir LUDARYX", "Open LUDARYX", "Open LUDARYX", "Abrir LUDARYX", "Abrir LUDARYX"),
        new("Encerrar", "Terminar", "Exit", "Exit", "Salir", "Salir"),
        new("SELECIONAR", "SELECIONAR", "SELECT", "SELECT", "SELECCIONAR", "SELECCIONAR"),
        new("PERSONALIZAR", "PERSONALIZAR", "CUSTOMIZE", "CUSTOMISE", "PERSONALIZAR", "PERSONALIZAR"),

        new("Adicionar jogo", "Adicionar jogo", "Add game", "Add game", "Añadir juego", "Añadir juego"),
        new("ADICIONAR JOGO MANUALMENTE", "ADICIONAR JOGO MANUALMENTE", "ADD GAME MANUALLY", "ADD GAME MANUALLY", "AÑADIR JUEGO MANUALMENTE", "AÑADIR JUEGO MANUALMENTE"),
        new("EDITAR CONFIGURAÇÕES DE INICIALIZAÇÃO", "EDITAR CONFIGURAÇÕES DE ARRANQUE", "EDIT LAUNCH SETTINGS", "EDIT LAUNCH SETTINGS", "EDITAR CONFIGURACIÓN DE INICIO", "EDITAR CONFIGURACIÓN DE INICIO"),
        new("Pasta de trabalho (opcional)", "Pasta de trabalho (opcional)", "Working directory (optional)", "Working directory (optional)", "Carpeta de trabajo (opcional)", "Carpeta de trabajo (opcional)"),
        new("Executar como administrador", "Executar como administrador", "Run as administrator", "Run as administrator", "Ejecutar como administrador", "Ejecutar como administrador"),
        new("Você pode alterar estas opções novamente pela tela de detalhes do jogo. Preencha um executável ou uma URI.", "Pode alterar estas opções novamente no ecrã de detalhes do jogo. Preencha um executável ou URI.", "You can change these options again from the game details screen. Enter an executable or URI.", "You can change these options again from the game details screen. Enter an executable or URI.", "Puedes cambiar estas opciones de nuevo desde la pantalla de detalles del juego. Indica un ejecutable o una URI.", "Puedes cambiar estas opciones de nuevo desde la pantalla de detalles del juego. Indica un ejecutable o una URI."),

        new("Nome", "Nome", "Name", "Name", "Nombre", "Nombre"),
        new("Executável (.exe)", "Executável (.exe)", "Executable (.exe)", "Executable (.exe)", "Ejecutable (.exe)", "Ejecutable (.exe)"),
        new("PROCURAR", "PROCURAR", "BROWSE", "BROWSE", "BUSCAR", "BUSCAR"),
        new("Argumentos de inicialização (opcional)", "Argumentos de arranque (opcional)", "Launch arguments (optional)", "Launch arguments (optional)", "Argumentos de inicio (opcional)", "Argumentos de inicio (opcional)"),
        new("URI de inicialização (opcional)", "URI de arranque (opcional)", "Launch URI (optional)", "Launch URI (optional)", "URI de inicio (opcional)", "URI de inicio (opcional)"),
        new("Preencha o executável ou uma URI. A URI é útil para jogos que usam um launcher específico.", "Preencha o executável ou um URI. O URI é útil para jogos que usam um launcher específico.", "Enter an executable or URI. A URI is useful for games that use a specific launcher.", "Enter an executable or URI. A URI is useful for games that use a specific launcher.", "Indica un ejecutable o una URI. La URI es útil para juegos que usan un launcher específico.", "Indica un ejecutable o una URI. La URI es útil para juegos que usan un launcher específico."),
        new("ADICIONAR", "ADICIONAR", "ADD", "ADD", "AÑADIR", "AÑADIR"),

        new("Detalhes do jogo", "Detalhes do jogo", "Game details", "Game details", "Detalles del juego", "Detalles del juego"),
        new("CAPA VERTICAL", "CAPA VERTICAL", "VERTICAL COVER", "VERTICAL COVER", "CARÁTULA VERTICAL", "CARÁTULA VERTICAL"),
        new("CAPA HORIZONTAL", "CAPA HORIZONTAL", "HORIZONTAL COVER", "HORIZONTAL COVER", "CARÁTULA HORIZONTAL", "CARÁTULA HORIZONTAL"),
        new("☆ FAVORITO", "☆ FAVORITO", "☆ FAVORITE", "☆ FAVOURITE", "☆ FAVORITO", "☆ FAVORITO"),
        new("JOGAR", "JOGAR", "PLAY", "PLAY", "JUGAR", "JUGAR"),
        new("ABRINDO JOGO", "A ABRIR JOGO", "OPENING GAME", "OPENING GAME", "ABRIENDO JUEGO", "ABRIENDO JUEGO"),
        new("Preparando inicialização...", "A preparar arranque...", "Preparing launch...", "Preparing launch...", "Preparando inicio...", "Preparando inicio..."),
        new("Finalizando abertura...", "A finalizar abertura...", "Finishing launch...", "Finishing launch...", "Finalizando apertura...", "Finalizando apertura..."),
        new("Aguardando o jogo...", "A aguardar o jogo...", "Waiting for the game...", "Waiting for the game...", "Esperando el juego...", "Esperando el juego..."),
        new("Jogo iniciado", "Jogo iniciado", "Game started", "Game started", "Juego iniciado", "Juego iniciado"),
        new("PROGRESSO", "PROGRESSO", "PROGRESS", "PROGRESS", "PROGRESO", "PROGRESO"),
        new("INTERROMPER", "INTERROMPER", "CANCEL", "CANCEL", "INTERRUMPIR", "INTERRUMPIR"),
        new("Interrompendo...", "A interromper...", "Cancelling...", "Cancelling...", "Interrumpiendo...", "Interrumpiendo..."),
        new("Inicialização interrompida", "Arranque interrompido", "Launch cancelled", "Launch cancelled", "Inicio interrumpido", "Inicio interrumpido"),
        new("O LUDARYX manterá esta tela aberta até detectar o processo do jogo.", "O LUDARYX manterá este ecrã aberto até detetar o processo do jogo.", "LUDARYX will keep this screen open until the game process is detected.", "LUDARYX will keep this screen open until the game process is detected.", "LUDARYX mantendrá esta pantalla abierta hasta detectar el proceso del juego.", "LUDARYX mantendrá esta pantalla abierta hasta detectar el proceso del juego."),
        new("Se o jogo não abrir e o Windows Security mostrar uma notificação, permita o aplicativo e tente novamente.", "Se o jogo não abrir e a Segurança do Windows mostrar uma notificação, permita a aplicação e tente novamente.", "If the game does not open and Windows Security shows a notification, allow the app and try again.", "If the game does not open and Windows Security shows a notification, allow the app and try again.", "Si el juego no abre y Seguridad de Windows muestra una notificación, permite la aplicación e inténtalo de nuevo.", "Si el juego no abre y Seguridad de Windows muestra una notificación, permite la aplicación e inténtalo de nuevo."),
        new("OCULTAR", "OCULTAR", "HIDE", "HIDE", "OCULTAR", "OCULTAR"),
        new("EXCLUIR DA BIBLIOTECA", "REMOVER DA BIBLIOTECA", "REMOVE FROM LIBRARY", "REMOVE FROM LIBRARY", "ELIMINAR DE LA BIBLIOTECA", "ELIMINAR DE LA BIBLIOTECA"),
        new("EDITAR INICIALIZAÇÃO", "EDITAR ARRANQUE", "EDIT LAUNCH", "EDIT LAUNCH", "EDITAR INICIO", "EDITAR INICIO"),
        new("TORNAR ESTA VERSÃO PRINCIPAL", "TORNAR ESTA VERSÃO PRINCIPAL", "MAKE THIS THE PRIMARY VERSION", "MAKE THIS THE PRIMARY VERSION", "HACER ESTA LA VERSIÓN PRINCIPAL", "HACER ESTA LA VERSIÓN PRINCIPAL"),
        new("VERSÃO PRINCIPAL", "VERSÃO PRINCIPAL", "PRIMARY VERSION", "PRIMARY VERSION", "VERSIÓN PRINCIPAL", "VERSIÓN PRINCIPAL"),
        new("VERSÃO PRINCIPAL DA DUPLICATA", "VERSÃO PRINCIPAL DO DUPLICADO", "PRIMARY DUPLICATE VERSION", "PRIMARY DUPLICATE VERSION", "VERSIÓN PRINCIPAL DEL DUPLICADO", "VERSIÓN PRINCIPAL DEL DUPLICADO"),
        new("VERSÃO SECUNDÁRIA", "VERSÃO SECUNDÁRIA", "SECONDARY VERSION", "SECONDARY VERSION", "VERSIÓN SECUNDARIA", "VERSIÓN SECUNDARIA"),


        new("Classificação", "Classificação", "Rating", "Rating", "Clasificación", "Clasificación"),
        new("Desenvolvedor / Publicadora", "Programador / Editora", "Developer / Publisher", "Developer / Publisher", "Desarrollador / Editor", "Desarrollador / Editor"),
        new("Lançamento", "Lançamento", "Release", "Release", "Lanzamiento", "Lanzamiento"),
        new("Descrição", "Descrição", "Description", "Description", "Descripción", "Descripción"),
        new("Classificação manual", "Classificação manual", "Manual metadata", "Manual metadata", "Clasificación manual", "Clasificación manual"),
        new("Gêneros separados por vírgula", "Géneros separados por vírgula", "Genres separated by commas", "Genres separated by commas", "Géneros separados por comas", "Géneros separados por comas"),
        new("Desenvolvedor", "Programador", "Developer", "Developer", "Desarrollador", "Desarrollador"),
        new("Publicadora", "Editora", "Publisher", "Publisher", "Editor", "Editor"),
        new("Artes personalizadas", "Artes personalizadas", "Custom artwork", "Custom artwork", "Arte personalizado", "Arte personalizado"),
        new("As imagens escolhidas são copiadas para a pasta do LUDARYX e não dependem do arquivo original.", "As imagens escolhidas são copiadas para a pasta do LUDARYX e não dependem do ficheiro original.", "Selected images are copied to the LUDARYX folder and do not depend on the original file.", "Selected images are copied to the LUDARYX folder and do not depend on the original file.", "Las imágenes seleccionadas se copian a la carpeta de LUDARYX y no dependen del archivo original.", "Las imágenes seleccionadas se copian a la carpeta de LUDARYX y no dependen del archivo original."),
        new("ESCOLHER CAPA VERTICAL", "ESCOLHER CAPA VERTICAL", "CHOOSE VERTICAL COVER", "CHOOSE VERTICAL COVER", "ELEGIR CARÁTULA VERTICAL", "ELEGIR CARÁTULA VERTICAL"),
        new("RESTAURAR VERTICAL", "RESTAURAR VERTICAL", "RESTORE VERTICAL", "RESTORE VERTICAL", "RESTAURAR VERTICAL", "RESTAURAR VERTICAL"),
        new("ESCOLHER ARTE HORIZONTAL", "ESCOLHER ARTE HORIZONTAL", "CHOOSE HORIZONTAL ART", "CHOOSE HORIZONTAL ART", "ELEGIR ARTE HORIZONTAL", "ELEGIR ARTE HORIZONTAL"),
        new("RESTAURAR HORIZONTAL", "RESTAURAR HORIZONTAL", "RESTORE HORIZONTAL", "RESTORE HORIZONTAL", "RESTAURAR HORIZONTAL", "RESTAURAR HORIZONTAL"),
        new("BUSCAR CAPA VERTICAL", "PROCURAR CAPA VERTICAL", "FETCH VERTICAL COVER", "FETCH VERTICAL COVER", "BUSCAR CARÁTULA VERTICAL", "BUSCAR CARÁTULA VERTICAL"),
        new("BUSCAR ARTE HORIZONTAL", "PROCURAR ARTE HORIZONTAL", "FETCH HORIZONTAL ART", "FETCH HORIZONTAL ART", "BUSCAR ARTE HORIZONTAL", "BUSCAR ARTE HORIZONTAL"),
        new("CORRIGIR JOGO DO STEAMGRIDDB", "CORRIGIR JOGO DO STEAMGRIDDB", "CORRECT STEAMGRIDDB GAME", "CORRECT STEAMGRIDDB GAME", "CORREGIR JUEGO DE STEAMGRIDDB", "CORREGIR JUEGO DE STEAMGRIDDB"),
        new("SALVAR CLASSIFICAÇÃO MANUAL", "GUARDAR CLASSIFICAÇÃO MANUAL", "SAVE MANUAL METADATA", "SAVE MANUAL METADATA", "GUARDAR CLASIFICACIÓN MANUAL", "GUARDAR CLASIFICACIÓN MANUAL"),
        new("FECHAR", "FECHAR", "CLOSE", "CLOSE", "CERRAR", "CERRAR"),

        new("SOBRE / CRÉDITOS", "SOBRE / CRÉDITOS", "ABOUT / CREDITS", "ABOUT / CREDITS", "ACERCA DE / CRÉDITOS", "ACERCA DE / CRÉDITOS"),
        new("Sobre / Créditos", "Sobre / Créditos", "About / Credits", "About / Credits", "Acerca de / Créditos", "Acerca de / Créditos"),
        new("Sobre", "Sobre", "About", "About", "Acerca de", "Acerca de"),
        new("Desenvolvimento com auxílio de IA", "Desenvolvimento com auxílio de IA", "Development with AI assistance", "Development with AI assistance", "Desarrollo con ayuda de IA", "Desarrollo con ayuda de IA"),
        new("O LUDARYX foi desenvolvido com o auxílio de ferramentas de inteligência artificial, incluindo o ChatGPT, em tarefas como geração e revisão de código, correção de erros, documentação, refinamento da interface e organização do projeto.", "O LUDARYX foi desenvolvido com o auxílio de ferramentas de inteligência artificial, incluindo o ChatGPT, em tarefas como geração e revisão de código, correção de erros, documentação, refinamento da interface e organização do projeto.", "LUDARYX was developed with the assistance of artificial intelligence tools, including ChatGPT, for tasks such as code generation and review, debugging, documentation, interface refinement, and project organization.", "LUDARYX was developed with the assistance of artificial intelligence tools, including ChatGPT, for tasks such as code generation and review, debugging, documentation, interface refinement, and project organisation.", "LUDARYX fue desarrollado con la ayuda de herramientas de inteligencia artificial, incluido ChatGPT, en tareas como generación y revisión de código, corrección de errores, documentación, refinamiento de la interfaz y organización del proyecto.", "LUDARYX fue desarrollado con la ayuda de herramientas de inteligencia artificial, incluido ChatGPT, en tareas como generación y revisión de código, corrección de errores, documentación, refinamiento de la interfaz y organización del proyecto."),
        new("A direção do projeto, as decisões de funcionalidades, os testes, a validação das builds e a responsabilidade final pelo software permanecem com o mantenedor do projeto.", "A direção do projeto, as decisões de funcionalidades, os testes, a validação das builds e a responsabilidade final pelo software permanecem com o mantenedor do projeto.", "The project's direction, feature decisions, testing, build validation, and final responsibility for the software remain with the project maintainer.", "The project's direction, feature decisions, testing, build validation, and final responsibility for the software remain with the project maintainer.", "La dirección del proyecto, las decisiones sobre funcionalidades, las pruebas, la validación de las compilaciones y la responsabilidad final del software permanecen a cargo del mantenedor del proyecto.", "La dirección del proyecto, las decisiones sobre funcionalidades, las pruebas, la validación de las compilaciones y la responsabilidad final del software permanecen a cargo del mantenedor del proyecto."),
        new("Fontes de dados", "Fontes de dados", "Data sources", "Data sources", "Fuentes de datos", "Fuentes de datos"),
        new("Licenças e atribuições", "Licenças e atribuições", "Licenses and attribution", "Licences and attribution", "Licencias y atribuciones", "Licencias y atribuciones"),
        new("Privacidade", "Privacidade", "Privacy", "Privacy", "Privacidad", "Privacidad"),
        new("Diagnóstico", "Diagnóstico", "Diagnostics", "Diagnostics", "Diagnóstico", "Diagnóstico"),
        new("ABRIR PASTA DE LOGS", "ABRIR PASTA DE LOGS", "OPEN LOG FOLDER", "OPEN LOG FOLDER", "ABRIR CARPETA DE LOGS", "ABRIR CARPETA DE LOGS"),
        new("COPIAR DIAGNÓSTICO", "COPIAR DIAGNÓSTICO", "COPY DIAGNOSTICS", "COPY DIAGNOSTICS", "COPIAR DIAGNÓSTICO", "COPIAR DIAGNÓSTICO"),
        new("VERIFICAR ATUALIZAÇÕES", "VERIFICAR ATUALIZAÇÕES", "CHECK FOR UPDATES", "CHECK FOR UPDATES", "BUSCAR ACTUALIZACIONES", "BUSCAR ACTUALIZACIONES"),
        new("Arquivos incluídos", "Ficheiros incluídos", "Included files", "Included files", "Archivos incluidos", "Archivos incluidos"),
        new("Fonte dos metadados", "Fonte dos metadados", "Metadata source", "Metadata source", "Fuente de metadatos", "Fuente de metadatos"),

        new("Escolher arte do SteamGridDB", "Escolher arte do SteamGridDB", "Choose SteamGridDB artwork", "Choose SteamGridDB artwork", "Elegir arte de SteamGridDB", "Elegir arte de SteamGridDB"),
        new("ESCOLHER ARTE", "ESCOLHER ARTE", "CHOOSE ARTWORK", "CHOOSE ARTWORK", "ELEGIR ARTE", "ELEGIR ARTE"),
        new("USAR ESTA ARTE", "USAR ESTA ARTE", "USE THIS ARTWORK", "USE THIS ARTWORK", "USAR ESTE ARTE", "USAR ESTE ARTE"),
        new("USAR SELECIONADA", "USAR SELECIONADA", "USE SELECTED", "USE SELECTED", "USAR SELECCIONADA", "USAR SELECCIONADA"),
        new("Escolher jogo no SteamGridDB", "Escolher jogo no SteamGridDB", "Choose SteamGridDB game", "Choose SteamGridDB game", "Elegir juego en SteamGridDB", "Elegir juego en SteamGridDB"),
        new("ESCOLHER JOGO DO STEAMGRIDDB", "ESCOLHER JOGO DO STEAMGRIDDB", "CHOOSE STEAMGRIDDB GAME", "CHOOSE STEAMGRIDDB GAME", "ELEGIR JUEGO DE STEAMGRIDDB", "ELEGIR JUEGO DE STEAMGRIDDB"),
        new("PESQUISAR", "PESQUISAR", "SEARCH", "SEARCH", "BUSCAR", "BUSCAR"),
        new("VERIFICADO", "VERIFICADO", "VERIFIED", "VERIFIED", "VERIFICADO", "VERIFICADO"),
        new("USAR SELECIONADO", "USAR SELECIONADO", "USE SELECTED", "USE SELECTED", "USAR SELECCIONADO", "USAR SELECCIONADO")
    ];

    public static void SetLanguage(string? languageCode)
    {
        CurrentLanguage = SupportedLanguages.Any(x => x.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            ? languageCode!
            : PortugueseBrazil;
    }

    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        if (TryTranslateSupplemental(text, out var supplemental))
            return supplemental;

        var translation = FindTranslation(text);
        return translation is null ? text : SelectLanguage(translation);
    }

    public static string Format(string baseText, params object[] args) =>
        string.Format(Translate(baseText), args);

    public static string FormatPlayCount(int count) => CurrentLanguage switch
    {
        JapaneseJapan => $"{count}回",
        EnglishUnitedStates or EnglishUnitedKingdom => count == 1 ? "1 launch" : $"{count} launches",
        SpanishLatinAmerica or SpanishEurope => count == 1 ? "1 partida" : $"{count} partidas",
        _ => count == 1 ? "1 partida" : $"{count} partidas"
    };

    public static string FormatLaunchCount(int count) => CurrentLanguage switch
    {
        JapaneseJapan => $"{count}回起動",
        EnglishUnitedStates or EnglishUnitedKingdom => count == 1 ? "1 launch" : $"{count} launches",
        SpanishLatinAmerica or SpanishEurope => count == 1 ? "1 inicio" : $"{count} inicios",
        _ => count == 1 ? "1 inicialização" : $"{count} inicializações"
    };

    public static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
            return CurrentLanguage == JapaneseJapan ? "0分" : "0 min";

        var duration = TimeSpan.FromSeconds(seconds);
        if (CurrentLanguage == JapaneseJapan)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}時間 {duration.Minutes}分";
            return $"{Math.Max(1, duration.Minutes)}分";
        }

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}min";
        return $"{Math.Max(1, duration.Minutes)} min";
    }

    public static string FormatLastPlayed(DateTime? value)
    {
        if (!value.HasValue)
            return Translate("Nunca jogado");

        var cultureName = CurrentLanguage switch
        {
            JapaneseJapan => "ja-JP",
            PortuguesePortugal => "pt-PT",
            EnglishUnitedStates => "en-US",
            EnglishUnitedKingdom => "en-GB",
            SpanishLatinAmerica => "es-MX",
            SpanishEurope => "es-ES",
            _ => "pt-BR"
        };

        try
        {
            return value.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(cultureName));
        }
        catch
        {
            return value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
    }

    public static string FormatStatisticsSummary(int games, int launches, string totalTime) => CurrentLanguage switch
    {
        JapaneseJapan => $"{games}本のゲーム • {launches}回起動 • 記録時間 {totalTime}",
        EnglishUnitedStates or EnglishUnitedKingdom => $"{games} games • {launches} launches • {totalTime} recorded",
        SpanishLatinAmerica or SpanishEurope => $"{games} juegos • {launches} inicios • {totalTime} registrados",
        PortuguesePortugal => $"{games} jogos • {launches} inicializações • {totalTime} registados",
        _ => $"{games} jogos • {launches} inicializações • {totalTime} de tempo registrado"
    };

    public static void Apply(DependencyObject root)
    {
        ApplyToObject(root);

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
                Apply(dependencyObject);
        }
    }

    private static void ApplyToObject(DependencyObject item)
    {
        if (item is Window window)
            window.Title = Translate(window.Title);

        if (item is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty) &&
            textBlock.Inlines.Count <= 1)
        {
            textBlock.Text = Translate(textBlock.Text);
        }

        if (item is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = Translate(content);

        if (item is HeaderedContentControl headered && headered.Header is string header)
            headered.Header = Translate(header);

        if (item is FrameworkElement frameworkElement && frameworkElement.ToolTip is string tooltip)
            frameworkElement.ToolTip = Translate(tooltip);
    }

    private static bool TryTranslateSupplemental(string text, out string translated)
    {
        var normalized = text.Trim();
        foreach (var item in SupplementalTranslations)
        {
            if (!MatchesSupplemental(item, normalized))
                continue;

            translated = CurrentLanguage switch
            {
                PortuguesePortugal => item.PtPt,
                EnglishUnitedStates => item.EnUs,
                EnglishUnitedKingdom => item.EnGb,
                SpanishLatinAmerica => item.Es419,
                SpanishEurope => item.EsEs,
                JapaneseJapan => item.JaJp,
                _ => item.PtBr
            };
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static bool MatchesSupplemental(SupplementalTranslation item, string text) =>
        text.Equals(item.PtBr, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.PtPt, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EnUs, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EnGb, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.Es419, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.EsEs, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(item.JaJp, StringComparison.Ordinal);

    private static Translation? FindTranslation(string text)
    {
        var normalizedText = text.Trim();

        // Permite sair do japonês para qualquer outro idioma sem deixar textos
        // já traduzidos presos na interface.
        var japaneseSource = JapaneseTranslations.FirstOrDefault(pair =>
            normalizedText.Equals(pair.Value, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(japaneseSource.Key))
        {
            var japaneseMatch = Translations.FirstOrDefault(x =>
                japaneseSource.Key.Equals(x.PtBr, StringComparison.Ordinal));
            if (japaneseMatch is not null)
                return japaneseMatch;
        }

        // Mantém a intenção visual de rótulos em caixa alta quando existem
        // traduções específicas para eles. O fallback sem diferenciar maiúsculas
        // continua útil para textos legados e conteúdo já traduzido.
        var exact = Translations.FirstOrDefault(x =>
            normalizedText.Equals(x.PtBr, StringComparison.Ordinal) ||
            normalizedText.Equals(x.PtPt, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EnUs, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EnGb, StringComparison.Ordinal) ||
            normalizedText.Equals(x.Es419, StringComparison.Ordinal) ||
            normalizedText.Equals(x.EsEs, StringComparison.Ordinal));

        if (exact is not null)
            return exact;

        return Translations.FirstOrDefault(x =>
            normalizedText.Equals(x.PtBr, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.PtPt, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EnUs, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EnGb, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.Es419, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Equals(x.EsEs, StringComparison.OrdinalIgnoreCase));
    }

    private static string SelectLanguage(Translation translation) => CurrentLanguage switch
    {
        PortuguesePortugal => translation.PtPt,
        EnglishUnitedStates => translation.EnUs,
        EnglishUnitedKingdom => translation.EnGb,
        SpanishLatinAmerica => translation.Es419,
        SpanishEurope => translation.EsEs,
        JapaneseJapan => JapaneseTranslations.TryGetValue(translation.PtBr, out var japanese)
            ? japanese
            : translation.PtBr,
        _ => translation.PtBr
    };
}
