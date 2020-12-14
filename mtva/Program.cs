using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace mtva
{
    class Program
    {
        public static bool highq = false;
        static void Main(string[] args)
        {
            List<string> downloadQueue = new List<string>();

            foreach (var arg in args)
            {
                if (arg.Contains("-q="))
                {
                    string quality = arg.Split('=')[1];
                    if (quality == "high")
                    {
                        highq = true;
                    }
                }
                else
                {
                    downloadQueue.Add(arg);
                }
            }

            foreach (var item in downloadQueue)
            {
                processVideo(item);
            }
            Console.WriteLine("All downloads complete.");
            Console.WriteLine("Press any button to close.");
            Console.ReadKey();
        }

        static void processVideo(string id)
        {
            using (WebClient client = new WebClient())
            {
                string vidthing = client.DownloadString("https://archivum.mtva.hu/m3/open/embed/?id=" + id);
                Console.WriteLine("Fetching Page...");

                string vidid = vidthing.Split(new string[] { "<div id=\"embed_player\" data-item='{\"id\":\"" }, StringSplitOptions.None)[1];
                vidid = vidid.Split(new string[] { "\",\"hasSubtitle" }, StringSplitOptions.None)[0];

                //string vidName = vidthing.Split(new string[] { "<meta property=\"og:title\" content=\"" }, StringSplitOptions.None)[1];
                //vidName = vidName.Split(new string[] { "\" />" }, StringSplitOptions.None)[0];

                //Console.WriteLine("Found Title: " + vidName);
                Console.WriteLine("Found Video ID: " + vidid);
                string vidpl = client.DownloadString("https://archivum.mtva.hu/m3/stream-v2?target=" + vidid + "&embed=true");
                targetJson vidjson = JsonConvert.DeserializeObject<targetJson>(vidpl);
                var availableRes = masterPlaylist(vidjson.Url);
                Console.WriteLine("Downloading Video...");
                downloadVideo(availableRes, id);
            }
        }

        static List<Streams> masterPlaylist(string masterUrl)
        {
            List<Streams> progStreams = new List<Streams>();

            try
            {
                WebClient client = new WebClient();
                client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4354.0 Safari/537.36");
                using (var stream = client.OpenRead(masterUrl))
                {
                    string resolution = "";
                    int bandwidth = 0;

                    string line;
                    StreamReader file = new StreamReader(stream);
                    while ((line = file.ReadLine()) != null)
                    {
                        if (line.StartsWith("#EXT-X-STREAM-INF") || line.Contains(".m3u8"))
                        {
                            if (line.StartsWith("#EXT-X-STREAM-INF"))
                            {
                                string[] words = line.Split(',');

                                foreach (var word in words)
                                {
                                    if (word.Contains("BANDWIDTH"))
                                    {
                                        bandwidth = Convert.ToInt32(word.Split('=')[1]);
                                    }
                                    if (word.Contains("RESOLUTION"))
                                    {
                                        resolution = word.Split('=')[1];
                                    }
                                }
                            }
                            else if (line.Contains(".m3u8"))
                            {
                                Streams streams = new Streams()
                                {
                                    Bandwidth = bandwidth,
                                    Resolution = resolution,
                                    Playlist = line,
                                };

                                progStreams.Add(streams);
                            }
                        }
                    }
                }
                return progStreams;
            }
            catch (WebException we)
            {
                return null;
            }
        }

        static void downloadVideo(List<Streams> streams, string vidName)
        {
            string winner = "";
            if (highq == true)
            {
                Console.WriteLine("Downloading highest quality.");
                winner = streams[streams.Count - 1].Playlist;
            }
            else
            {
                Console.WriteLine("These resolutions are the available: ");
                for (int i = 0; i < streams.Count; i++)
                {
                    Console.WriteLine("Stream {0}: {1}", i, streams[i].Resolution);
                }
                Console.WriteLine("Enter the stream number you wish to download: ");
                int num = Convert.ToInt32(Console.ReadLine());

                winner = streams[num].Playlist;
            }

            var chunkInfo = getChunks(winner);

            try
            {
                //fake iv
                byte[] iv = { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };

                char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
                vidName = new string(vidName.Where(ch => !invalidFileNameChars.Contains(ch)).ToArray());

                if (File.Exists(vidName + ".ts"))
                {
                    Console.WriteLine(vidName + " already exists, skipping.");
                    return;
                }

                using (var outputStream = File.Create(vidName + ".ts"))
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4354.0 Safari/537.36");

                        RijndaelManaged algorithm = new RijndaelManaged()
                        {
                            Padding = PaddingMode.PKCS7,
                            Mode = CipherMode.CBC,
                            KeySize = 128,
                            BlockSize = 128,
                            Key = chunkInfo.Key,
                            IV = iv
                        };

                        int chunknum = 1;
                        foreach (var chunk in chunkInfo.Streams)
                        {
                            Console.Write("\rDownloading Part: " + chunknum + " of " + chunkInfo.Streams.Count);
                            chunknum++;
                            var inputStream = client.DownloadData(chunk);

                            using (MemoryStream ms = new MemoryStream())
                            {
                                using (CryptoStream cs = new CryptoStream(ms, algorithm.CreateDecryptor(), CryptoStreamMode.Write))
                                {
                                    cs.Write(inputStream, 0, inputStream.Length);
                                    cs.FlushFinalBlock();

                                    byte[] bytes = ms.ToArray();
                                    outputStream.Write(bytes, 0, bytes.Length);
                                }
                            }
                        }
                    }
                }

                Console.WriteLine("\nDownload Complete: " + vidName);
            }
            catch (Exception)
            {
            }
        }

        static Chunks getChunks(string videoPlaylist)
        {
            Chunks chunks = new Chunks();
            List<string> tsChunks = new List<string>();

            using (var client = new WebClient())
            {
                client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4354.0 Safari/537.36");
                Stream playlist = client.OpenRead(videoPlaylist);
                StreamReader file = new StreamReader(playlist);

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    if (line.StartsWith("#EXT-X-KEY"))
                    {
                        string[] words = line.Split(',');

                        foreach (var word in words)
                        {
                            if (word.Contains("URI"))
                            {
                                string keyUrl = word.Replace("URI=", "");
                                keyUrl = keyUrl.Replace("\"", "");
                                chunks.Key = downloadKey(keyUrl);
                                break;
                            }
                        }
                    }
                    else if (line.Contains(".ts"))
                    {
                        tsChunks.Add(line);
                    }
                }
                file.Close();
                chunks.Streams = tsChunks;

            }
            return chunks;
        }

        static byte[] downloadKey(string keyUrl)
        {
            using (var client = new WebClient())
            {
                Console.WriteLine("Downloading Key");
                client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4354.0 Safari/537.36");
                byte[] keyBytes = client.DownloadData(keyUrl);
                return keyBytes;
            }
        }

    }
}
