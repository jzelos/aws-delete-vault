using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.Glacier;
using Amazon.Glacier.Model;
using Amazon.Glacier.Transfer;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace AWS
{
    class Program
    {

        static void Main(string[] args)
        {
            string vaultName = "myVault";
            string inventoryPath = String.Concat(@"c:\temp\", vaultName, ".csv");

            // Request inventory
            if (!File.Exists(inventoryPath))
                GetFreshInventory(vaultName, inventoryPath);

            // parse inventory and delete all archives
            using (var reader = new StreamReader(inventoryPath))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var fields = line.Split(',');
                    string archiveId = fields[0];
                    if (!String.IsNullOrEmpty(archiveId))
                    {
                        if (!DeleteArchive(vaultName, archiveId))
                        {
                            Console.WriteLine(String.Format("Could not delete archive {0}, retrying", archiveId));
                            Console.WriteLine("Sleeping for 30 seconds");
                            if (!DeleteArchive(vaultName, archiveId))
                                Console.WriteLine(String.Format("Could not delete archive {0}, failed", archiveId));
                        }
                    }
                }
            }

            // delete vault
            if (DeleteVault(vaultName))
                Console.WriteLine(String.Format("Deleted vault {0}", vaultName));
            else
                Console.WriteLine(String.Format("Could not delete vault {0}", vaultName));
        }


        private static bool DeleteArchive(string vaultName, string archiveId)
        {
            try
            {
                using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
                {
                    DeleteArchiveRequest request = new DeleteArchiveRequest()
                    {
                        VaultName = vaultName,
                        ArchiveId = archiveId
                    };
                    DeleteArchiveResponse response = client.DeleteArchive(request);
                    return (response.HttpStatusCode == System.Net.HttpStatusCode.NoContent);
                }
            }
            catch (ResourceNotFoundException)
            {
                return true; // already removed
            }
        }

        private static bool DeleteVault(string vaultName)
        {
            using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
            {
                DeleteVaultRequest request = new DeleteVaultRequest()
                {
                    VaultName = vaultName
                };
                DeleteVaultResponse response = client.DeleteVault(request);                
                return (response.HttpStatusCode == System.Net.HttpStatusCode.NoContent);
            }
        }

        #region Inventory
        
        private static void GetFreshInventory(string vaultName, string inventoryPath)
        {
            GlacierJobDescription job = null;
            string jobId = null;

            // Check for recent job to use
            Console.WriteLine("Listing jobs");
            var jobs = ListInventory(vaultName);
            if (jobs.Count() > 0)
            {
                job = jobs.Where(j => j.Completed && j.CreationDate > DateTime.Now.AddDays(-7)).OrderByDescending(j => j.CreationDate).FirstOrDefault();
                if (job != null)                
                    jobId = job.JobId;                
            }

            // if no job, ask for one and poll for completion
            if (jobId == null)
            {
                
                // See if any pending jobs are awaiting before requesting new
                job = jobs.Where(j => !j.Completed && j.CreationDate > DateTime.Now.AddDays(-7)).OrderByDescending(j => j.CreationDate).FirstOrDefault();
                if (job == null)
                {
                    Console.WriteLine("Requesting new inventory");
                    jobId = RequestInventory(vaultName);
                }
                else
                    jobId = job.JobId;

                while (!IsInventoryReady(jobId, vaultName))
                {
                    Console.WriteLine("Inventory not ready, sleeping for 30 minutes");
                    Thread.Sleep(30 * 60 * 1000);
                }
            }

            // Save inventory to disc
            Console.WriteLine("Saved inventory to disc");
            GetInventory(jobId, vaultName, inventoryPath);
        }

        private static List<GlacierJobDescription> ListInventory(string vaultName)
        {
            using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
            {
                ListJobsRequest request = new ListJobsRequest()
                {
                    VaultName = vaultName
                };

                ListJobsResponse response = client.ListJobs(request);
                return response.JobList;
            }
        }

        private static string RequestInventory(string vaultName)
        {
            using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
            {
                InitiateJobRequest initJobRequest = new InitiateJobRequest()
                {
                    VaultName = vaultName,
                    JobParameters = new JobParameters()
                    {
                        Type = "inventory-retrieval",
                        Format = "CSV"
                    }
                };
                InitiateJobResponse initJobResponse = client.InitiateJob(initJobRequest);
                return initJobResponse.JobId;
            }
        }

        private static bool IsInventoryReady(string jobId, string vaultName)
        {
            using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
            {
                DescribeJobRequest request = new DescribeJobRequest()
                {
                    JobId = jobId,
                    VaultName = vaultName
                };

                DescribeJobResponse response = client.DescribeJob(request);
                return response.Completed;
            }
        }

        private static void GetInventory(string jobId, string vaultName, string filename)
        {
            using (IAmazonGlacier client = new AmazonGlacierClient(RegionEndpoint.EUWest1))
            {
                GetJobOutputRequest getJobOutputRequest = new GetJobOutputRequest()
                {
                    JobId = jobId,
                    VaultName = vaultName,
                };

                GetJobOutputResponse getJobOutputResponse = client.GetJobOutput(getJobOutputRequest);
                using (Stream webStream = getJobOutputResponse.Body)
                {
                    using (Stream fileToSave = File.OpenWrite(filename))
                    {
                        CopyStream(webStream, fileToSave);
                    }
                }

            }
        }

        #endregion

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[65536];
            int length;
            while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, length);
            }
        }

        public static string getHashSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            SHA256Managed hashstring = new SHA256Managed();
            byte[] hash = hashstring.ComputeHash(bytes);
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            return hashString;
        }

    }
}
