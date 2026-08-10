# FLASHIda

## Description

FLASHIda is an intelligent data acquisition method for top-down proteomics, built for Thermo Scientific tribrids. It ensures the real-time selection of high-quality precursors of diverse proteforms, using an instant m/z-intensity to mass-quality spectral transformation coupled with a machine learning-based quality assessment.

## Usage

FLASHIda runs as a command-line tool. While running it takes the control over the acquistion of mass spectra, i.e. which spectra will be acquired and in which order.
The acquisition parameters can be specified using a XML-formatted method file, an example of it is provided along with the tool.

The following optional arguments can be used
```
Options:
  -h, --help                 Usage information
  -v, --version              Show version information
  -o, --nocc                 Ignore contact closure. Default: false
  -t, --test                 Run in test mode without connection to the instrument. Default: false
  -m, --method=VALUE         Location of method file. Default: method.xml in
                               the program folder
  -r, --rawname=VALUE        Name or path of the raw file. Used to prefix the
                               timestamped run folder that holds every log
                               file. If not specified the folder is named by
                               the timestamp alone
```

All log files — the two log4net logs and the engine's five TSV/text streams — are written into one
per-run folder, `<runtime.log_dir>/<rawname>_<timestamp>/`, sharing a single timestamp.
`runtime.log_dir` is set in the method file and defaults to the working directory. The folder also
receives a verbatim copy of the method file, always named `method.json`, so a run folder records the
exact config that produced it and can be re-run as-is.

> **Note:** `Usage.pdf` still describes the older behaviour ("If any of the log files exist, a
> timestamp will be added to the filename"). Timestamps are now unconditional and live in the
> folder name, so log files can no longer collide.
Advanced usage is discussed in [here](Usage.pdf)

## Installation

### Requirements

 * **Thermo Scientific tribrid instrument**, i.e. Orbitrap Fusion, Orbitrap Fusion Lumos, Orbitrap Eclipse, with Tune version 3.4
 * **Instrument API** - https://github.com/thermofisherlsms/iapi - the API and the license should be obtained separately from Thermo
 * **.NET 4.8+**
 * **OpenMS libraries**

[Detailed installation and building instructions](Installation.md)

## Publication

Jeong, K., Babović, M., Gorshkov, V. et al. FLASHIda enables intelligent data acquisition for top–down proteomics to boost proteoform identification counts. Nat Commun 13, 4407 (2022). https://doi.org/10.1038/s41467-022-31922-z
