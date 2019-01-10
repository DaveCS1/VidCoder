﻿using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using VidCoderCommon.Model;

namespace VidCoderCommon
{
	[ServiceContract(SessionMode = SessionMode.Required,
		CallbackContract = typeof(IHandBrakeWorkerCallback))]
	public interface IHandBrakeWorker
	{
		[OperationContract]
		void SetUpWorker(
			int verbosity,
			int previewCount,
			bool useDvdNav,
			double minTitleDurationSeconds,
			double cpuThrottlingFraction,
			string tempFolder);

		[OperationContract]
		void StartScan(
			string path);

		[OperationContract]
		void StartEncode(
			VCJob job,
			int previewNumber,
			int previewSeconds,
			string defaultChapterNameFormat);

        /// <summary>
        /// Starts an encode with the given encode JSON.
        /// </summary>
        /// <param name="encodeJson">The encode JSON.</param>
		[OperationContract]
        void StartEncodeFromJson(string encodeJson);

        [OperationContract]
		void PauseEncode();

		[OperationContract]
		void ResumeEncode();

		[OperationContract]
		void StopEncode();

		[OperationContract]
		string Ping();
	}
}
