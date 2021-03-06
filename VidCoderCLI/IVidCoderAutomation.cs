﻿namespace VidCoderCLI
{
	public interface IVidCoderAutomation
	{
		void Encode(string source, string destination, string preset, string picker);

		void Scan(string source);

		void ImportPreset(string filePath);

		void ImportQueue(string filePath);
	}
}