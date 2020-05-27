﻿namespace CarefulAudioRepair.Processing
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Immutable;
    using System.Threading.Tasks;
    using CarefulAudioRepair.Data;

    class ScannerTools
    {
        private readonly BlockingCollection<AbstractPatch> patchCollection;
        private readonly ImmutableArray<double> input;
        private readonly IAudioProcessingSettings settings;
        private ImmutableArray<double> predictionErr;

        public ScannerTools(ImmutableArray<double> inputSamples, IAudioProcessingSettings settings)
        {
            this.patchCollection = new BlockingCollection<AbstractPatch>();

            this.input = inputSamples;

            this.InputPatcher = new Patcher(
                this.input,
                this.patchCollection,
                (patch, position) => patch.GetValue(position));

            this.Predictor = new FastBurgPredictor(
                settings.CoefficientsNumber,
                settings.HistoryLengthSamples);

            this.NormCalculator = new AveragedMaxErrorAnalyzer();
        }

        public bool IsPreprocessed { get; private set; } = false;

        public IDetector DamageDetector { get; private set; }

        public IPredictor Predictor { get; }

        public IAnalyzer NormCalculator { get; }

        internal IPatcher PredictionErrPatcher { get; private set; }

        internal IPatchMaker PatchMaker { get; private set; }

        internal IRegenerator Regenerarator { get; private set; }

        internal IPatcher InputPatcher { get; }

        public void GetReady(
            IProgress<string> status,
            IProgress<double> progress)
        {
            status.Report("Preparation");
            progress.Report(0);

            var errors = this.CalculatePredictionErrors(progress);

            // Initialize fields that depend on prediction errors
            this.predictionErr = ImmutableArray.Create(errors);
            this.PredictionErrPatcher = new Patcher(
                this.predictionErr,
                this.patchCollection,
                (_, __) => AbstractPatch.MinimalPredictionError);

            this.DamageDetector = new DamagedSampleDetector(
                this.PredictionErrPatcher,
                this.InputPatcher,
                this.NormCalculator,
                this.Predictor);

            this.Regenerarator = new Regenerator(
                this.InputPatcher,
                this.Predictor,
                this.DamageDetector);

            this.PatchMaker = new PatchMaker(this.Regenerarator);

            progress.Report(100);

            // The fields are initialized
            this.IsPreprocessed = true;
        }

        private double[] CalculatePredictionErrors(
            IProgress<double> progress)
        {
            var errors = new double[this.input.Length];

            var inputDataSize = this.Predictor.InputDataSize;

            var start = inputDataSize;
            var end = this.input.Length;

            if (start >= end)
            {
                return errors;
            }

            var chunkSize = Math.Max(
                inputDataSize,
                (end - start) / Environment.ProcessorCount);

            var part = Partitioner.Create(start, end, chunkSize);

            Parallel.ForEach(part, (range, state, index) =>
            {
                for (var position = range.Item1; position < range.Item2; position++)
                {
                    var inputDataStart = position - inputDataSize;

                    errors[position] = this.input[position]
                        - this.Predictor.GetForward(
                            this.InputPatcher.GetRange(inputDataStart, inputDataSize));

                    // Only the first thread reports
                    // Throttling by 1000 samples
                    if (index == 0 && position % 1000 == 0)
                    {
                        progress.Report(
                            100.0 * (position - range.Item1)
                            / (range.Item2 - range.Item1));
                    }
                }
            });

            return errors;
        }
    }
}
