﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Renci.SshNet.Common;
using Renci.SshNet.Tests.Properties;

namespace Renci.SshNet.Tests.SftpClientTests
{
    /// <summary>
    /// Summary description for CreateDirectoryTest
    /// </summary>
    [TestClass]
    public class CreateDirectoryTest
    {
        [TestInitialize()]
        public void CleanCurrentFolder()
        {
            using (var client = new SshClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                client.Connect();
                client.RunCommand("rm -rf *");
                client.Disconnect();
            }
        }

        [TestMethod]
        [TestCategory("Sftp")]
        [ExpectedException(typeof(SshConnectionException))]
        public void Test_Sftp_CreateDirectory_Without_Connecting()
        {
            using (var sftp = new SftpClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                sftp.CreateDirectory("test");
            }
        }

        [TestMethod]
        [TestCategory("Sftp")]
        public void Test_Sftp_CreateDirectory_In_Current_Location()
        {
            using (var sftp = new SftpClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                sftp.Connect();

                sftp.CreateDirectory("test");

                sftp.Disconnect();
            }
        }

        [TestMethod]
        [TestCategory("Sftp")]
        [ExpectedException(typeof(SshPermissionDeniedException))]
        public void Test_Sftp_CreateDirectory_In_Forbidden_Directory()
        {
            using (var sftp = new SftpClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                sftp.Connect();

                sftp.CreateDirectory("/sbin/test");

                sftp.Disconnect();
            }
        }

        [TestMethod]
        [TestCategory("Sftp")]
        [ExpectedException(typeof(SshFileNotFoundException))]
        public void Test_Sftp_CreateDirectory_Invalid_Path()
        {
            using (var sftp = new SftpClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                sftp.Connect();

                sftp.CreateDirectory("/abcdefg/abcefg");

                sftp.Disconnect();
            }
        }

        [TestMethod]
        [TestCategory("Sftp")]
        public void Test_Sftp_CreateDirectory_Already_Exists()
        {
            using (var sftp = new SftpClient(Resources.HOST, Resources.USERNAME, Resources.PASSWORD))
            {
                sftp.Connect();

                sftp.CreateDirectory("test");

                var exceptionThrown = false;
                try
                {
                    sftp.CreateDirectory("test");
                }
                catch (SshException)
                {
                    exceptionThrown = true;
                }

                Assert.IsTrue(exceptionThrown);

                sftp.Disconnect();
            }
        }
    }
}
