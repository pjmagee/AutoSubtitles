// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Patrick Magee">
//   Copyright © 2013 Patrick Magee
//   
//   This program is free software: you can redistribute it and/or modify it
//   under the +terms of the GNU General Public License as published by 
//   the Free Software Foundation, either version 3 of the License, 
//   or (at your option) any later version.
//   
//   This program is distributed in the hope that it will be useful, 
//   but WITHOUT ANY WARRANTY; without even the implied warranty of 
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the 
//   GNU General Public License for more details.
//   
//   You should have received a copy of the GNU General Public License
//   along with this program. If not, see http://www.gnu.org/licenses/.
// </copyright>
// <summary>
//   The program.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace NSub
{
    #region

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Serialization;

    #endregion

    /// <summary>
    /// The program.
    /// </summary>
    public class Program
    {
        #region Constants

        /// <summary>
        /// The action to perform on the API.
        /// </summary>
        private const string Action = "download";

        /// <summary>
        /// The user agent
        /// </summary>
        private const string UserAgent = @"SubDB/1.0 (NSub/0.1; https://github.com/pjmagee/NSub)";

        /// <summary>
        /// The read size.
        /// <remarks>
        /// The amount to read either side of the file.
        /// </remarks>
        /// </summary>
        private const int ReadSize = 64 * 1024;

        #endregion

        #region Static Fields

        /// <summary>
        /// The reader writer lock.
        /// </summary>
        private static readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        /// <summary>
        /// The reset event slim
        /// </summary>
        private static readonly ManualResetEventSlim ResetEventSlim = new ManualResetEventSlim(false);

        /// <summary>
        /// The token source.
        /// <remarks>
        /// This is passed to our download loop. If the user manually wants to cancel, this token source
        /// will be triggered and will cancel the download operation safely. Including saving the cache.
        /// </remarks>
        /// </summary>
        private static readonly CancellationTokenSource TokenSource = new CancellationTokenSource();

        /// <summary>
        /// The cache.
        /// <remarks>
        /// The key is our file and the value is our hashed result of the file.
        /// </remarks>
        /// </summary>
        private static ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// The downloaded.
        /// </summary>
        private static volatile int downloaded;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the cache path.
        /// <remarks>
        /// Cache is used so that once the webservice returns a 404 for a particular file. We store this result
        /// in cache so that we do not hammer the webservice for something we know does not exist. Deleting the
        /// cache will result in checking all items without subtitles again.
        /// </remarks>
        /// </summary>
        /// <value>
        /// The cache path.
        /// </value>
        public static string CachePath
        {
            get
            {
                return ConfigurationManager.AppSettings.Get("Cache.Path");
            }
        }

        /// <summary>
        /// Gets the log file.
        /// <remarks>
        /// Contains a datetime stamp and lists the downloaded subtitles.
        /// </remarks>
        /// </summary>
        /// <value>
        /// The log file.
        /// </value>
        public static string LogFile
        {
            get
            {
                return ConfigurationManager.AppSettings.Get("Log.Downloaded.Path");
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// The language for the subtitles
        /// </summary>
        /// <value>
        /// The language.
        /// </value>
        private static string Languages 
        {
            get
            {
                return ConfigurationManager.AppSettings.Get("Subtitle.Languages") ?? "en,us";
            }
        }

        /// <summary>
        /// The parent movie directory
        /// </summary>
        /// <value>
        /// The parent movie directory.
        /// </value>
        private static string ParentMovieDirectory
        {
            get
            {
                return ConfigurationManager.AppSettings.Get("Root.Movies.Path");
            }
        }

        /// <summary>
        /// The parent directory
        /// </summary>
        /// <value>
        /// The parent tv show directory.
        /// </value>
        private static string ParentTvShowDirectory
        {
            get
            {
                return ConfigurationManager.AppSettings.Get("Root.Shows.Path");
            }
        }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        public static void Main()
        {
            CheckConfiguration();

            Console.ResetColor();
            Console.WriteLine("Press CTRL + C to cancel.");
            Console.WriteLine();

            LoadCache();

            RegisterCancel();

            DownloadSubtitles(ParentTvShowDirectory);

            DownloadSubtitles(ParentMovieDirectory);

            SaveCache();

            PrintSummary();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Adds the resulting file and hash to cache.
        /// </summary>
        /// <param name="file">
        /// The file.
        /// </param>
        /// <param name="hash">
        /// The hash.
        /// </param>
        private static void AddToCache(string file, string hash)
        {
            if (!cache.ContainsKey(file))
            {
                cache.TryAdd(file, hash);
            }
        }

        /// <summary>
        /// Checks the configuration.
        /// </summary>
        private static void CheckConfiguration()
        {
            bool isValid = true;

            Console.WriteLine("Reading config.");
            

            if (!File.Exists(LogFile))
            {
                File.WriteAllText(LogFile, DateTime.Now + Environment.NewLine);
            }

            if (!Directory.Exists(ParentTvShowDirectory))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("TV shows path does not exist.");
                isValid = false;
            }

            if (!Directory.Exists(ParentMovieDirectory))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Movies path does not exist.");
                isValid = false;
            }

            if (!isValid)
            {
                Console.ResetColor();
                Console.WriteLine("Fix configuration and try again.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadLine();
                Environment.Exit(Environment.ExitCode);
            }
        }

        /// <summary>
        /// The create md5 digest.
        /// </summary>
        /// <param name="data">
        /// The data.
        /// </param>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        private static string CreateMD5Hash(byte[] data)
        {
            var sb = new StringBuilder();

            using (var md5 = MD5.Create())
            {
                foreach (byte chunk in md5.ComputeHash(data))
                {
                    sb.Append(chunk.ToString("x2"));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Downloads the subtitles.
        /// </summary>
        /// <param name="rootDirectory">The root directory.</param>
        private static void DownloadSubtitles(string rootDirectory)
        {
            try
            {
                var files = Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories); // get the files once.

                var episodes = files.Where(f => IsMediaFile(f) && !cache.ContainsKey(f)).ToArray();
                
                var options = new ParallelOptions 
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount, 
                    CancellationToken = TokenSource.Token
                };

                Parallel.ForEach(episodes, options, file =>
                {
                    string subtitle = file.Remove(file.Length - 4) + ".srt";

                    if (!files.Contains(subtitle) && !cache.ContainsKey(file))
                    {
                        TryDownload(file, subtitle);
                    }
                });

            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
            }
            finally
            {
                ResetEventSlim.Set(); // We're ready.
            }
        }

        /// <summary>
        /// Computes the md5 using this specific hashing algorihtm.
        /// <remarks>
        /// It takes the first 64kb of the file and the last 64kb of the file,
        /// attaches them together and hashes the result.
        /// </remarks>
        /// </summary>
        /// <param name="file">The file to be hashed.</param>
        /// <returns>
        /// The MD5 hex digest.
        /// </returns>
        private static string GetMD5FromFile(string file)
        {
            try
            {
                // hashing algorithm implementation
                using (var stream = File.Open(file, FileMode.Open, FileAccess.Read))
                {
                    // Beginning of the file
                    var beginning = new byte[ReadSize];
                    var read = stream.Read(beginning, 0, ReadSize);
                    
                    // Seek to the end and set position - readsize
                    stream.Seek(-read, SeekOrigin.End);

                    // Read end of the file
                    var end = new byte[ReadSize];
                    stream.Read(end, 0, ReadSize);

                    // Concat the data
                    byte[] data = beginning.Concat(end).ToArray();

                    // Compute the hash
                    return CreateMD5Hash(data);
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
                Thread.Sleep(5000);
                return GetMD5FromFile(file);
            }
        }

        /// <summary>
        /// Determines whether [is media file] [the specified file].
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns>
        /// True if [is media file] otherwise False.
        /// </returns>
        private static bool IsMediaFile(string file)
        {
            return file.EndsWith(".mkv") || file.EndsWith(".mp4") || file.EndsWith(".avi");
        }

        /// <summary>
        /// Loads the cache.
        /// </summary>
        private static void LoadCache()
        {
            try
            {
                using (var reader = XmlReader.Create(CachePath, new XmlReaderSettings { IgnoreComments = true, CheckCharacters = false }))
                {
                    var hashedItems = new XmlSerializer(typeof(List<HashedItem>)).Deserialize(reader) as List<HashedItem>;

                    if (hashedItems != null)
                    {
                        cache = new ConcurrentDictionary<string, string>(hashedItems.ToDictionary(i => i.Path, i => i.Hash));
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.WriteLine("Loaded {0} cached results.", cache.Count);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Prints the summary.
        /// </summary>
        private static void PrintSummary()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Downloaded {0} subtitles", downloaded);
            Console.ResetColor();
        }

        /// <summary>
        /// Registers the cancel event.
        /// </summary>
        private static void RegisterCancel()
        {
            Console.CancelKeyPress += (sender, args) =>
                {
                    args.Cancel = false;

                    Console.WriteLine("Cancelling.");
                    Console.WriteLine();

                    CancelAndWait();

                    SaveCache();

                    PrintSummary();

                    Console.ReadLine();
                    Environment.Exit(Environment.ExitCode);
                };
        }

        private static void CancelAndWait()
        {
            TokenSource.Cancel();
            Console.WriteLine("Waiting for processes to finish up.");
            ResetEventSlim.Wait();
            Console.WriteLine("Done.");
            Console.WriteLine();
        }

        /// <summary>
        /// Saves the cache.
        /// </summary>
        private static void SaveCache()
        {
            try
            {
                // create our serialized object from our cache dictionary.
                var cachedItems = cache.Select(pair => new HashedItem { Hash = pair.Value, Path = pair.Key }).ToList();

                using (var writer = XmlWriter.Create(CachePath, new XmlWriterSettings { Indent = true }))
                {
                    new XmlSerializer(typeof(List<HashedItem>)).Serialize(writer, cachedItems);
                }

                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.WriteLine("Saved {0} cached results.", cache.Count);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Tries to download.
        /// </summary>
        /// <param name="file">
        /// The file.
        /// </param>
        /// <param name="subtitle">
        /// The subtitle.
        /// </param>
        private static void TryDownload(string file, string subtitle)
        {
            string calculatedHash = GetMD5FromFile(file);

            try
            {
                var url = string.Format("http://api.thesubdb.com/?action={0}&hash={1}&language={2}", Action, calculatedHash, Languages);

                HttpWebRequest request = WebRequest.CreateHttp(url);
                request.UserAgent = UserAgent;

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var stream = response.GetResponseStream())
                        {
                            var subtitles = new StreamReader(stream, Encoding.Default).ReadToEnd();

                            File.WriteAllText(subtitle, subtitles);

                            downloaded++;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Downloaded subtitles: {0}", Path.GetFileName(subtitle));

                            Lock.EnterWriteLock();
                            File.AppendAllText(LogFile, subtitle + Environment.NewLine);
                            Lock.ExitWriteLock();
                        }
                    }
                }
            }
            catch (WebException e)
            {
                AddToCache(file, calculatedHash);

                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    var webResponse = e.Response as HttpWebResponse;

                    if (webResponse != null)
                    {
                        Console.ResetColor();
                        Console.WriteLine("Subtitles for {0}: {1}", Path.GetFileNameWithoutExtension(file), webResponse.StatusCode);
                    }
                }
            }
        }

        #endregion
    }
}
