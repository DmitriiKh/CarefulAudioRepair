﻿// <copyright file="Channel.cs" company="Dmitrii Khrustalev">
// Copyright (c) Dmitrii Khrustalev. All rights reserved.
// </copyright>

namespace CarefulAudioRepair.Data
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading.Tasks;
    using CarefulAudioRepair.Processing;

    /// <summary>
    /// Represents audio samples for one channel.
    /// </summary>
    internal class Channel : IDisposable
    {
        private readonly ImmutableArray<double> input;
        private readonly IAudioProcessingSettings settings;
        private IRegenerator regenerarator;
        private IPatcher inputPatcher;
        private IPatcher predictionErrPatcher;
        private BlockingCollection<AbstractPatch> patchCollection;

        /// <summary>
        /// Initializes a new instance of the <see cref="Channel"/> class.
        /// </summary>
        /// <param name="inputSamples">Input audio samples.</param>
        /// <param name="settings">Audio setting.</param>
        public Channel(double[] inputSamples, IAudioProcessingSettings settings)
        {
            if (inputSamples is null)
            {
                throw new ArgumentNullException(nameof(inputSamples));
            }

            this.patchCollection = new BlockingCollection<AbstractPatch>();

            this.input = ImmutableArray.Create(inputSamples);

            this.settings = settings;

            this.IsPreprocessed = false;
        }

        /// <summary>
        /// Gets a value indicating whether scan was performed once on this data
        /// so the prediction errors were calculated.
        /// </summary>
        public bool IsPreprocessed { get; private set; }

        /// <summary>
        /// Gets length of audio in samples.
        /// </summary>
        public int LengthSamples => this.input.Length;

        /// <summary>
        /// Gets number of patches.
        /// </summary>
        public int NumberOfPatches => this.patchCollection.Count;

        /// <summary>
        /// Asynchronously scans audio for damaged samples and repairs them.
        /// </summary>
        /// <param name="status">Parameter to report status through.</param>
        /// <param name="progress">Parameter to report progress through.</param>
        /// <returns>Task.</returns>
        public async Task ScanAsync(
            IProgress<string> status,
            IProgress<double> progress)
        {
            var scanner = new Scanner(this.input, this.settings);

            (this.patchCollection, this.inputPatcher, this.predictionErrPatcher) =
                await scanner.ScanAsync(status, progress).ConfigureAwait(false);

            foreach (var patch in this.patchCollection)
            {
                this.RegisterPatch(patch);
            }

            this.IsPreprocessed = true;
        }

        /// <summary>
        /// Returns array of patches generated by ScanAsync method.
        /// </summary>
        /// <returns>Array of patches.</returns>
        public Patch[] GetAllPatches()
        {
            var patchList = this.patchCollection.ToList();
            patchList.Sort();
            return patchList.Select(p => p as Patch).ToArray();
        }

        /// <summary>
        /// Returns value of input sample at position.
        /// </summary>
        /// <param name="position">Position of input sample.</param>
        /// <returns>Value.</returns>
        public double GetInputSample(int position) => this.input[position];

        /// <summary>
        /// Returns value of output sample at position.
        /// </summary>
        /// <param name="position">Position of output sample.</param>
        /// <returns>Value.</returns>
        public double GetOutputSample(int position) =>
            this.inputPatcher.GetValue(position);

        /// <summary>
        /// Returns value of prediction error at position.
        /// </summary>
        /// <param name="position">Position of prediction error.</param>
        /// <returns>Value.</returns>
        public double GetPredictionErr(int position) =>
            this.predictionErrPatcher.GetValue(position);

        /// <inheritdoc/>
        public void Dispose()
        {
            this.patchCollection.Dispose();
        }

        private void RemoveAllPatches()
        {
            while (this.patchCollection.TryTake(out _))
            {
            }
        }

        private void RegisterPatch(AbstractPatch patch)
        {
            patch.Updater += this.PatchUpdater;
        }

        private void PatchUpdater(object sender, EventArgs e) =>
            this.regenerarator.RestorePatch(sender as AbstractPatch);
    }
}