# FlashIDA

## Description

FlashIDA is an intelligent data acquisition method for top-down proteomics, built for Thermo Scientific tribrids. It ensures the real-time selection of high-quality precursors of diverse proteforms, using an instant m/z-intensity to mass-quality spectral transformation coupled with a machine learning-based quality assessment.

## Usage

FlashIDA runs as a command-line tool. While running it takes the control over the acquistion of mass spectra, i.e. which spectra will be acquired and in which order.
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
  -r, --rawname=VALUE        The name or path to raw file, that will be used to
                               name the log files. If not specified timestamp
                               will be used
```
Advanced usage is discussed in [here](Usage.pdf)

## Installation

### Requirements

 * **Thermo Scientific tribrid instrument**, i.e. Orbitrap Fusion, Orbitrap Fusion Lumos, Orbitrap Eclipse.
 * **Instrument API** - https://github.com/thermofisherlsms/iapi - the API and the license should be obtained separately from Thermo
 * **.NET 4.8+**
 * **OpenMS libraries**

[Detailed installation and building instructions](Installation.md)

## Publication

Publication pending