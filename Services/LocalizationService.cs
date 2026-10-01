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
    public const string SpanishLatinAmerica = "es-419";
    public const string SpanishEurope = "es-ES";

    public static string CurrentLanguage { get; private set; } = PortugueseBrazil;

    public static IReadOnlyList<(string Code, string Name)> SupportedLanguages { get; } =
    [
        (PortugueseBrazil, "Português (Brasil)"),
        (PortuguesePortugal, "Português (Portugal)"),
        (EnglishUnitedStates, "English (US)"),
        (EnglishUnitedKingdom, "English (UK)"),
        (SpanishLatinAmerica, "Español (Latinoamérica)"),
        (SpanishEurope, "Español (España)")
    ];

    private sealed record Translation(
        string PtBr,
        string PtPt,
        string EnUs,
        string EnGb,
        string Es419,
        string EsEs);

    private static readonly Translation[] Translations =
    [
        new("Configurações", "Definições", "Settings", "Settings", "Configuración", "Configuración"),
        new("CONFIGURAÇÕES", "DEFINIÇÕES", "SETTINGS", "SETTINGS", "CONFIGURACIÓN", "CONFIGURACIÓN"),
        new("Controle a biblioteca, capas, integrações e itens ocultos.", "Controle a biblioteca, capas, integrações e itens ocultos.", "Manage the library, artwork, integrations, and hidden items.", "Manage the library, artwork, integrations, and hidden items.", "Administra la biblioteca, carátulas, integraciones y elementos ocultos.", "Administra la biblioteca, carátulas, integraciones y elementos ocultos."),
        new("Biblioteca", "Biblioteca", "Library", "Library", "Biblioteca", "Biblioteca"),
        new("Mostrar Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store", "Show Xbox / Microsoft Store", "Show Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store", "Mostrar Xbox / Microsoft Store"),
        new("Mostrar itens ocultos na biblioteca", "Mostrar itens ocultos na biblioteca", "Show hidden items in library", "Show hidden items in library", "Mostrar elementos ocultos en la biblioteca", "Mostrar elementos ocultos en la biblioteca"),
        new("Ocultar painel lateral e hero", "Ocultar painel lateral e hero", "Hide side panel and hero", "Hide side panel and hero", "Ocultar panel lateral y hero", "Ocultar panel lateral y hero"),
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
        new("Pontuação: ", "Pontuação: ", "Score: ", "Score: ", "Puntuación: ", "Puntuación: "),
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

        var translation = FindTranslation(text);
        return translation is null ? text : SelectLanguage(translation);
    }

    public static string Format(string baseText, params object[] args) =>
        string.Format(Translate(baseText), args);

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

    private static Translation? FindTranslation(string text)
    {
        var normalizedText = text.Trim();

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
        _ => translation.PtBr
    };
}
