using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Windows.Forms;
using YamlDotNet.RepresentationModel;

namespace LittleBuddy
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtPath.Text = folderBrowserDialog1.SelectedPath;
        }

        private async void btnInstall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPath.Text))
            {
                MessageBox.Show("Wybierz folder instalacji");
                return;
            }

            btnInstall.Enabled = false;

            try
            {
                string installDir = txtPath.Text;
                Directory.CreateDirectory(installDir);

                string archivePath = Path.Combine(installDir, "rpcs3.7z");
                string rpcs3Dir = Path.Combine(installDir, "rpcs3");
                Directory.CreateDirectory(rpcs3Dir);

                string url = await GetLatestRpcs3DownloadUrl();
                await DownloadFileAsync(url, archivePath);
                Extract7z(archivePath, rpcs3Dir);
                File.Delete(archivePath);

                string configPath = Path.Combine(rpcs3Dir, "config", "config.yml");
                UpdateRpcs3Config(configPath);

                MessageBox.Show("RPCS3 zainstalowany i skonfigurowany!");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                btnInstall.Enabled = true;
            }
        }

        async Task<string> GetLatestRpcs3DownloadUrl()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LittleBuddy/1.0");

            var json = await client.GetStringAsync(
                "https://api.github.com/repos/RPCS3/rpcs3-binaries-win/releases"
            );

            using var doc = JsonDocument.Parse(json);
            DateTime latestDate = DateTime.MinValue;
            string latestUrl = null;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var published = release.GetProperty("published_at").GetDateTime();
                if (published <= latestDate) continue;

                var assets = release.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString()?.ToLower();
                    if (name != null && name.EndsWith(".7z") && name.Contains("win") && name.Contains("64"))
                    {
                        latestDate = published;
                        latestUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (latestUrl == null)
                throw new Exception("Nie znaleziono aktualnego builda RPCS3");

            return latestUrl;
        }

        async Task DownloadFileAsync(string url, string outputPath)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LittleBuddy/1.0");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(fs);
        }

        void Extract7z(string archivePath, string outputDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = @"7z/7za.exe",
                Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var p = Process.Start(psi);
            p.WaitForExit();
        }

        void UpdateRpcs3Config(string configPath)
        {
            if (!File.Exists(configPath))
            {
                MessageBox.Show("Nie znaleziono config.yml, tworzę nowy.");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, "Core:\nVideo:\n"); // minimalny szkielet
            }

            var yaml = new YamlStream();
            using (var reader = new StreamReader(configPath))
                yaml.Load(reader);

            var root = (YamlMappingNode)yaml.Documents[0].RootNode;

            if (!root.Children.ContainsKey(new YamlScalarNode("Video")))
                root.Children[new YamlScalarNode("Video")] = new YamlMappingNode();
            var videoNode = (YamlMappingNode)root.Children[new YamlScalarNode("Video")];

            videoNode.Children[new YamlScalarNode("Write Color Buffers")] = new YamlScalarNode("true");
            videoNode.Children[new YamlScalarNode("Write Depth Buffer")] = new YamlScalarNode("true");
            videoNode.Children[new YamlScalarNode("Read Color Buffers")] = new YamlScalarNode("true");
            videoNode.Children[new YamlScalarNode("Read Depth Buffer")] = new YamlScalarNode("true");
            videoNode.Children[new YamlScalarNode("VSync")] = new YamlScalarNode("false");
            videoNode.Children[new YamlScalarNode("Multithreaded RSX")] = new YamlScalarNode("true");

            if (!root.Children.ContainsKey(new YamlScalarNode("Core")))
                root.Children[new YamlScalarNode("Core")] = new YamlMappingNode();
            var coreNode = (YamlMappingNode)root.Children[new YamlScalarNode("Core")];
            coreNode.Children[new YamlScalarNode("PPU Threads")] = new YamlScalarNode("4");

            using (var writer = new StreamWriter(configPath))
                yaml.Save(writer, assignAnchors: false);
        }
    }
}
