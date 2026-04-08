using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

namespace SaagiInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("------------------------------------------");
            Console.WriteLine("Iniciando configurador do sistema SAAGI...");
            Console.WriteLine("------------------------------------------\n");

            // 1. Validar se o XAMPP está instalado
            Console.WriteLine(">>> Validando Instalação do XAMPP...");
            if (!Directory.Exists(@"C:\xampp"))
            {
                Console.WriteLine("[ERRO] XAMPP não está instalado ou a pasta padrão (C:\\xampp) não foi encontrada.");
                Console.WriteLine("Por favor, instale o XAMPP antes de prosseguir com a configuração.");
                Console.WriteLine("Pressione qualquer tecla para sair...");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("[✓] XAMPP encontrado em C:\\xampp.\n");

            // 2. Validar e iniciar Apache e MySQL
            Console.WriteLine(">>> Validando Serviços do XAMPP");
            ValidateProcessAndStart("httpd", "Apache", @"C:\xampp\apache_start.bat");
            if (!ValidateProcessAndStart("mysqld", "MySQL", @"C:\xampp\mysql_start.bat"))
            {
                Console.WriteLine("\n[ERRO CRÍTICO] Falha ao iniciar o MySQL. A instalação não pode prosseguir.");
                Console.WriteLine("Pressione qualquer tecla para sair...");
                Console.ReadKey();
                return;
            }
            Console.WriteLine();

            // 3. Configurar permissão externa para a pasta Htdocs (httpd.conf)
            Console.WriteLine(">>> Configurando permissões do Apache para a pasta htdocs...");
            ConfigureApacheHtdocsPermissions();
            Console.WriteLine();

            // Setup de Arquivos: Copiar a pasta "SAAGI" para o diretório htdocs.
            // Para isso, assume-se que há uma pasta "SAAGI" junto de onde este programa estiver rodando.
            Console.WriteLine(">>> Copiando os Arquivos do Sistema...");
            string sourceSaagiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SAAGI");
            string htdocsSaagiPath = @"C:\xampp\htdocs\SAAGI"; // Caminho alvo do XAMPP

            if (Directory.Exists(sourceSaagiPath))
            {
                Console.WriteLine("Copiando pasta SAAGI para 'C:\\xampp\\htdocs\\SAAGI'...");
                try
                {
                    DirectoryCopy(sourceSaagiPath, htdocsSaagiPath, true);
                    Console.WriteLine("[✓] Arquivos copiados com sucesso.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha ao copiar a pasta: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[AVISO] A pasta 'SAAGI' não foi encontrada no mesmo local deste programa ({sourceSaagiPath}).");
                Console.WriteLine("Por favor, copie manualmente a pasta 'SAAGI' para 'C:\\xampp\\htdocs'.");
            }
            Console.WriteLine();

            // Coletar configurações do Sistema e Banco de dados uma única vez
            Console.WriteLine(">>> Configurações Gerais do Sistema");
            Console.Write("Por favor, digite a URL_BASE que será acessada (Deixe vazio para auto-detectar): ");
            string inUrlBase = Console.ReadLine() ?? "";
            string urlBase = inUrlBase.Trim();
            
            if (string.IsNullOrWhiteSpace(urlBase))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    urlBase = $"http://{Environment.MachineName}/SAAGI/";
                }
                else
                {
                    string ipLocal = ObterIpLocalDaMaquina();
                    urlBase = $"http://{ipLocal}/SAAGI/";
                }
                Console.WriteLine($"[INFO] URL_BASE gerada automaticamente como: {urlBase}");
            }

            Console.Write("Por favor, digite o nome do usuário do banco de dados que o sistema utilizará (Deixe vazio para o padrão 'root'): ");
            string dbAppUser = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(dbAppUser)) dbAppUser = "root";

            Console.Write($"Digite a senha para este usuário '{dbAppUser}' (Deixe vazio caso não possua senha): ");
            string dbAppPassword = Console.ReadLine() ?? "";
            
            Console.WriteLine("\n>>> Credenciais de Importação de Estrutura");
            Console.Write("Por favor, digite o nome de usuário ADMINISTRADOR do MySQL para criar o banco (Deixe vazio para o padrão 'root'): ");
            string dbAdminUser = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(dbAdminUser)) dbAdminUser = "root";

            Console.Write($"Digite a senha para o administrador '{dbAdminUser}' (Deixe vazio caso não possua senha): ");
            string dbAdminPassword = Console.ReadLine() ?? "";
            Console.WriteLine();

            // Setup de Arquivos: Ajustar "C:\xampp\htdocs\SAAGI\source\Config.php"
            Console.WriteLine(">>> Configurando as conexões no Config.php...");
            ConfigureSaagiConfig(htdocsSaagiPath, dbAppUser, dbAppPassword, urlBase);
            Console.WriteLine();

            // Banco de Dados: Importar banco "SAAGI DB.sql"
            // Ao invés do usar a interface visual, iremos importar silenciosamente via linha de comando!
            Console.WriteLine(">>> Importando Banco de Dados...");
            if (!ImportDatabase(sourceSaagiPath, dbAdminUser, dbAdminPassword))
            {
                Console.WriteLine("\n[ERRO CRÍTICO] A importação do banco de dados falhou devido a credenciais inválidas ou erro estrutural.");
                Console.WriteLine("A instalação não pode prosseguir. Pressione qualquer tecla para sair...");
                Console.ReadKey();
                return;
            }
            Console.WriteLine();

            Console.WriteLine("------------------------------------------");
            Console.WriteLine("Configuração do SAAGI concluída!");
            Console.WriteLine("Pressione qualquer tecla para sair...");
            Console.ReadKey();
        }

        static bool ValidateProcessAndStart(string processName, string displayName, string startScriptPath)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                Console.WriteLine($"[✓] Serviço {displayName} encontra-se ativo.");
                return true;
            }

            Console.WriteLine($"[!] Serviço {displayName} NÃO ESTÁ ATIVO! Tentando iniciar...");

            string lastError = "";
            string lastOutput = "";

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Console.WriteLine($"    Tentativa {attempt}/2 de iniciar {displayName}...");
                try
                {
                    if (!File.Exists(startScriptPath))
                    {
                        Console.WriteLine($"    [ERRO] Script de inicialização não encontrado: {startScriptPath}");
                        return false;
                    }

                    ProcessStartInfo startInfo = new ProcessStartInfo(startScriptPath)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    };
                    
                    using (Process proc = new Process())
                    {
                        proc.StartInfo = startInfo;

                        System.Text.StringBuilder outputBuilder = new System.Text.StringBuilder();
                        System.Text.StringBuilder errorBuilder = new System.Text.StringBuilder();

                        proc.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                        proc.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        
                        // Timeout de 5000ms (5 segundos). Se passar disso, segue executando (comportamento normal do XAMPP em background).
                        bool exited = proc.WaitForExit(5000);
                        
                        // Aguardar extra para ter certeza que os processos filhos subiram completamente.
                        Thread.Sleep(2000);

                        lastOutput = outputBuilder.ToString();
                        lastError = errorBuilder.ToString();
                    }

                    processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        Console.WriteLine($"[✓] Serviço {displayName} iniciado com sucesso na tentativa {attempt}.");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            Console.WriteLine($"[ERRO] Não foi possível iniciar o serviço {displayName} após 2 tentativas.");
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                Console.WriteLine($"    Erro retornado: {lastError.Trim()}");
            }
            else if (!string.IsNullOrWhiteSpace(lastOutput))
            {
                 Console.WriteLine($"    Saída: {lastOutput.Trim()}");
            }
            return false;
        }

        static void ConfigureApacheHtdocsPermissions()
        {
            string confPath = @"C:\xampp\apache\conf\httpd.conf";

            if (!File.Exists(confPath))
            {
                Console.WriteLine($"[ERRO] Arquivo de configuração '{confPath}' não localizado.\nVerifique se o XAMPP foi instalado corretamente.");
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(confPath);
                bool inDirectoryBlock = false;
                bool modified = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string lineTrimmed = lines[i].Trim();
                    
                    // Busca a abertura de <Directory "C:/xampp/htdocs">
                    if (lineTrimmed.StartsWith("<Directory \"C:/xampp/htdocs\">", StringComparison.OrdinalIgnoreCase) || 
                        lineTrimmed.StartsWith("<Directory \"C:/xampp/htdocs/\">", StringComparison.OrdinalIgnoreCase))
                    {
                        inDirectoryBlock = true;
                    }
                    else if (inDirectoryBlock && lineTrimmed.StartsWith("</Directory>", StringComparison.OrdinalIgnoreCase))
                    {
                        inDirectoryBlock = false;
                    }

                    if (inDirectoryBlock)
                    {
                        // Se estivermos dentro da flag de permissão e encontrar Require local, nós alteramos
                        if (lineTrimmed.StartsWith("Require local", StringComparison.OrdinalIgnoreCase) || 
                            (lineTrimmed.StartsWith("Require", StringComparison.OrdinalIgnoreCase) && !lineTrimmed.Contains("all granted", StringComparison.OrdinalIgnoreCase)))
                        {
                            lines[i] = "    Require all granted"; // Substitui a linha por Require all granted
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    File.WriteAllLines(confPath, lines);
                    Console.WriteLine("[✓] Arquivo 'httpd.conf' atualizado (Permissão 'Require all granted' concedida para 'htdocs').");
                    RestartApache();
                }
                else
                {
                    Console.WriteLine("[✓] As permissões de acesso ao 'htdocs' no Apache já estão como 'Require all granted'.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Houve uma falha ao modificar o arquivo httpd.conf: {ex.Message}");
            }
        }

        static void RestartApache()
        {
            Console.WriteLine("Reiniciando o Apache para aplicar as configurações...");

            string xamppStop = @"C:\xampp\apache_stop.bat";
            string xamppStart = @"C:\xampp\apache_start.bat";

            if (File.Exists(xamppStop) && File.Exists(xamppStart))
            {
                Process stopProc = Process.Start(new ProcessStartInfo(xamppStop) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                stopProc.WaitForExit();
                
                Thread.Sleep(2000); // Dar tempo ao processo para fechar
                
                Process.Start(new ProcessStartInfo(xamppStart) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                Console.WriteLine("[✓] O serviço Apache foi reiniciado e as atualizações foram aplicadas.");
            }
            else
            {
                Console.WriteLine("[AVISO] Para aplicar as permissões do php reinicie manualmente o serviço 'Apache' no Painel do XAMPP.");
            }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException("A origem do diretório não existe ou não foi achada: " + sourceDirName);

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destDirName);

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, true); // True fará a substitutivaçao dos arquivos caso eles já existam
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }

        static void ConfigureSaagiConfig(string saagiPath, string dbUser, string dbPassword, string urlBase)
        {
            string configPath = Path.Combine(saagiPath, @"source\Config.php");
            bool isFileExists = File.Exists(configPath);
            
            // Verifica na pasta do htdocs primeiro. Se não achar ou se ela não tiver sido copiada, tenta arrumar a versão local antes de copiar.
            if (!isFileExists)
            {
                configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"SAAGI\source\Config.php"); // Tenta alterar do diretório base providenciado
                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"[AVISO] Arquivo 'Config.php' não localizado em ({configPath}). Lembrete de ajustá-lo manualmente na pasta 'C:\\xampp\\htdocs\\SAAGI\\source\\Config.php'.");
                    return;
                }
            }

            try
            {
                string content = File.ReadAllText(configPath);

                // URL_BASE
                content = Regex.Replace(content, @"(['""]URL_BASE['""]\s*,\s*['""]).*?(['""])", $"${{1}}{urlBase}${{2}}", RegexOptions.IgnoreCase);

                // USUARIO ('root', etc)
                content = Regex.Replace(content, @"(['""]username['""]\s*=>\s*['""]).*?(['""])", $"${{1}}{dbUser}${{2}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"(['""]user['""]\s*=>\s*['""]).*?(['""])", $"${{1}}{dbUser}${{2}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"((?:public|protected|private)?\s*\$user(?:name)?\s*=\s*['""]).*?(['""])", $"${{1}}{dbUser}${{2}}", RegexOptions.IgnoreCase);

                // SENHA
                content = Regex.Replace(content, @"(['""]passwd['""]\s*=>\s*['""]).*?(['""])", $"${{1}}{dbPassword}${{2}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"(['""]password['""]\s*=>\s*['""]).*?(['""])", $"${{1}}{dbPassword}${{2}}", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"((?:public|protected|private)?\s*\$pass(?:wd|word)?\s*=\s*['""]).*?(['""])", $"${{1}}{dbPassword}${{2}}", RegexOptions.IgnoreCase);

                File.WriteAllText(configPath, content);
                Console.WriteLine($"[✓] As chaves de acesso ao 'Config.php' foram atualizadas (Usuário: {dbUser}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao tentar atualizar o arquivo 'Config.php': {ex.Message}");
            }
        }

        public static string ObterIpLocalDaMaquina()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    // Achar um ip ipv4 que em teoria seria o dá rede local real.
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        static bool ImportDatabase(string sourceSaagiPath, string dbUser, string dbPassword)
        {
            string sqlFile = Path.Combine(sourceSaagiPath, "SAAGI DB.sql");
            
            // Caso o arquivo SQL não seja encontrado no subdiretório de cópia, vai procurar na raiz da pasta inicial executável
            if (!File.Exists(sqlFile))
            {
                sqlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SAAGI DB.sql");
            }

            if (!File.Exists(sqlFile))
            {
                Console.WriteLine($"[ERRO] O banco predefinido SQL ({sqlFile}) não pôde ser encontrado.");
                return false;
            }

            string mysqlExe = @"C:\xampp\mysql\bin\mysql.exe";
            if (!File.Exists(mysqlExe))
            {
                Console.WriteLine("[ERRO] Arquivo não encontrado no sistema XAMPP: C:\\xampp\\mysql\\bin\\mysql.exe");
                Console.WriteLine("Por favor, importe o banco 'SAAGI DB.sql' via phpMyAdmin manualmente.");
                return false;
            }

            Console.WriteLine("Detectou-se o arquivo Base de Dados. Iniciando importação silenciosa...");

            string passwordArg = string.IsNullOrEmpty(dbPassword) ? "" : $"-p{dbPassword}";

            // Invocar terminal MySQL e injetar o SQL
            // -e manda o comando 'source file.sql' que irá rodar em cima do que se precisa no script SQL
            ProcessStartInfo procStartInfo = new ProcessStartInfo(mysqlExe, $"-u {dbUser} {passwordArg} -e \"source {sqlFile}\"");
            procStartInfo.RedirectStandardOutput = true;
            procStartInfo.RedirectStandardError = true;
            procStartInfo.UseShellExecute = false;
            procStartInfo.CreateNoWindow = true; // Rodar escondido, magicamente.

            try
            {
                Process process = Process.Start(procStartInfo);
                process.WaitForExit();

                string errorText = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("[✓] Sucesso! Banco de 'SAAGI DB.sql' importado diretamente em background.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[ERRO] A importação via terminal gerou falhas (Exit: {process.ExitCode}).");
                    if (!string.IsNullOrEmpty(errorText)) {
                        Console.WriteLine($"Logs de erro do MySQL: \n{errorText}");
                    }
                    Console.WriteLine("Recomendado confirmar se o BD já existe, ou importar manualmente em: http://localhost/phpmyadmin/");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Tentando acessar o utilitário mysql.exe: {ex.Message}");
                Console.WriteLine("Importe manualmente o arquivo no phpMyAdmin na Web.");
                return false;
            }
        }
    }
}
