"""
MicroPython driver for the ST LSM6DSO32 6-axis IMU (accelerometer + gyroscope).

Ported from Adafruit's CircuitPython LSM6DS library (adafruit_lsm6ds.lsm6dso32),
using the same registers, defaults and units:
  acceleration -> m/s^2      gyro -> rad/s      temperature -> deg C
Extras for convenience: gyro_dps (deg/s), calibrate_gyro(), raw readers.

Usage:
    from machine import I2C, Pin
    from lsm6dso32 import LSM6DSO32
    i2c = I2C(0, sda=Pin(4), scl=Pin(5), freq=400_000)
    imu = LSM6DSO32(i2c)          # auto-detects 0x6A / 0x6B
    print(imu.acceleration, imu.gyro)
"""

import struct
import time
from micropython import const

# ---- Registers -------------------------------------------------------------
_WHO_AM_I = const(0x0F)
_CTRL1_XL = const(0x10)   # ODR_XL[7:4] FS_XL[3:2] LPF2_XL_EN[1]
_CTRL2_G = const(0x11)    # ODR_G[7:4]  FS_G[3:2]  FS_125[1]
_CTRL3_C = const(0x12)    # BOOT[7] BDU[6] H_LACTIVE[5] PP_OD[4] SIM[3] IF_INC[2] - SW_RESET[0]
_CTRL8_XL = const(0x17)   # HPCF_XL[7:5] HP_REF_MODE[4] FASTSETTL[3] HP_SLOPE_XL_EN[2]
_CTRL9_XL = const(0x18)   # I3C_DISABLE[1]
_STATUS_REG = const(0x1E) # TDA[2] GDA[1] XLDA[0]
_OUT_TEMP_L = const(0x20)
_OUTX_L_G = const(0x22)
_OUTX_L_A = const(0x28)

_CHIP_ID = const(0x6C)
_DEFAULT_ADDRESSES = (0x6A, 0x6B)

_G_TO_MS2 = 9.80665
_DPS_TO_RADS = 0.017453292519943295

# ---- Data rates (same codes for accel & gyro) ------------------------------
RATE_SHUTDOWN = const(0)
RATE_12_5_HZ = const(1)
RATE_26_HZ = const(2)
RATE_52_HZ = const(3)
RATE_104_HZ = const(4)
RATE_208_HZ = const(5)
RATE_416_HZ = const(6)
RATE_833_HZ = const(7)
RATE_1_66K_HZ = const(8)
RATE_3_33K_HZ = const(9)
RATE_6_66K_HZ = const(10)
RATE_1_6_HZ = const(11)   # accelerometer low-power mode only

# ---- Accelerometer ranges (FS_XL codes; note DSO32's unusual ordering) -----
ACCEL_RANGE_4G = const(0)
ACCEL_RANGE_32G = const(1)
ACCEL_RANGE_8G = const(2)
ACCEL_RANGE_16G = const(3)
# mg per LSB
_ACCEL_SENS = {0: 0.122, 1: 0.976, 2: 0.244, 3: 0.488}

# ---- Gyro ranges (value written to CTRL2_G bits [3:1]) ----------------------
GYRO_RANGE_125_DPS = const(0b001)
GYRO_RANGE_250_DPS = const(0b000)
GYRO_RANGE_500_DPS = const(0b010)
GYRO_RANGE_1000_DPS = const(0b100)
GYRO_RANGE_2000_DPS = const(0b110)
# mdps per LSB
_GYRO_SENS = {0b001: 4.375, 0b000: 8.75, 0b010: 17.5, 0b100: 35.0, 0b110: 70.0}


class LSM6DSO32:
    def __init__(self, i2c, address=None):
        self.i2c = i2c
        self._b1 = bytearray(1)
        self._b2 = bytearray(2)
        self._b6 = bytearray(6)
        self._gyro_bias = (0.0, 0.0, 0.0)  # in dps

        if address is None:
            found = i2c.scan()
            for a in _DEFAULT_ADDRESSES:
                if a in found:
                    address = a
                    break
            else:
                raise OSError(
                    "LSM6DSO32 not found. I2C scan: %s"
                    % [hex(a) for a in found]
                )
        self.address = address

        chip = self._read_u8(_WHO_AM_I)
        if chip != _CHIP_ID:
            raise RuntimeError(
                "Wrong chip at 0x%02X: WHO_AM_I=0x%02X (expected 0x6C)"
                % (address, chip)
            )

        self.reset()
        self._set_bits(_CTRL3_C, 1 << 6, 1 << 6)  # BDU: don't mix old/new halves
        self._set_bits(_CTRL3_C, 1 << 2, 1 << 2)  # IF_INC: auto-increment address
        self._set_bits(_CTRL9_XL, 1 << 1, 1 << 1) # disable I3C (as Adafruit does)

        # Same defaults as the CircuitPython driver
        self.accelerometer_range = ACCEL_RANGE_4G
        self.accelerometer_data_rate = RATE_104_HZ
        self.gyro_range = GYRO_RANGE_250_DPS
        self.gyro_data_rate = RATE_104_HZ

    # ---- low-level helpers -------------------------------------------------
    def _read_u8(self, reg):
        self.i2c.readfrom_mem_into(self.address, reg, self._b1)
        return self._b1[0]

    def _write_u8(self, reg, val):
        self._b1[0] = val & 0xFF
        self.i2c.writeto_mem(self.address, reg, self._b1)

    def _set_bits(self, reg, mask, value):
        cur = self._read_u8(reg)
        self._write_u8(reg, (cur & ~mask) | (value & mask))

    def _read_xyz(self, reg):
        self.i2c.readfrom_mem_into(self.address, reg, self._b6)
        return struct.unpack("<hhh", self._b6)

    # ---- control -------------------------------------------------------------
    def reset(self):
        """Software reset; restores all control registers to defaults."""
        self._write_u8(_CTRL3_C, 0x01)
        for _ in range(100):
            if not (self._read_u8(_CTRL3_C) & 0x01):
                break
            time.sleep_ms(1)
        time.sleep_ms(10)

    @property
    def accelerometer_range(self):
        return (self._read_u8(_CTRL1_XL) >> 2) & 0x03

    @accelerometer_range.setter
    def accelerometer_range(self, value):
        if value not in _ACCEL_SENS:
            raise ValueError("invalid accelerometer range")
        self._set_bits(_CTRL1_XL, 0x0C, value << 2)
        self._accel_sens = _ACCEL_SENS[value]

    @property
    def accelerometer_data_rate(self):
        return self._read_u8(_CTRL1_XL) >> 4

    @accelerometer_data_rate.setter
    def accelerometer_data_rate(self, value):
        if not 0 <= value <= 11:
            raise ValueError("invalid data rate")
        self._set_bits(_CTRL1_XL, 0xF0, value << 4)

    @property
    def gyro_range(self):
        return (self._read_u8(_CTRL2_G) >> 1) & 0x07

    @gyro_range.setter
    def gyro_range(self, value):
        if value not in _GYRO_SENS:
            raise ValueError("invalid gyro range")
        self._set_bits(_CTRL2_G, 0x0E, value << 1)
        self._gyro_sens = _GYRO_SENS[value]

    @property
    def gyro_data_rate(self):
        return self._read_u8(_CTRL2_G) >> 4

    @gyro_data_rate.setter
    def gyro_data_rate(self, value):
        if not 0 <= value <= 10:  # 1.6 Hz code not valid for gyro
            raise ValueError("invalid data rate")
        self._set_bits(_CTRL2_G, 0xF0, value << 4)

    @property
    def high_pass_filter(self):
        """Accel high-pass cutoff code 0-7 (CTRL8_XL HPCF_XL)."""
        return self._read_u8(_CTRL8_XL) >> 5

    @high_pass_filter.setter
    def high_pass_filter(self, value):
        self._set_bits(_CTRL8_XL, 0xE0, (value & 0x07) << 5)

    # ---- status ----------------------------------------------------------------
    @property
    def acceleration_ready(self):
        return bool(self._read_u8(_STATUS_REG) & 0x01)

    @property
    def gyro_ready(self):
        return bool(self._read_u8(_STATUS_REG) & 0x02)

    # ---- data ------------------------------------------------------------------
    @property
    def raw_acceleration(self):
        return self._read_xyz(_OUTX_L_A)

    @property
    def raw_gyro(self):
        return self._read_xyz(_OUTX_L_G)

    @property
    def acceleration(self):
        """(x, y, z) in m/s^2."""
        k = self._accel_sens * _G_TO_MS2 / 1000.0
        x, y, z = self._read_xyz(_OUTX_L_A)
        return (x * k, y * k, z * k)

    @property
    def gyro_dps(self):
        """(x, y, z) in degrees/second, with calibrate_gyro() bias removed."""
        k = self._gyro_sens / 1000.0
        x, y, z = self._read_xyz(_OUTX_L_G)
        bx, by, bz = self._gyro_bias
        return (x * k - bx, y * k - by, z * k - bz)

    @property
    def gyro(self):
        """(x, y, z) in rad/s (same units as the CircuitPython driver)."""
        x, y, z = self.gyro_dps
        return (x * _DPS_TO_RADS, y * _DPS_TO_RADS, z * _DPS_TO_RADS)

    @property
    def temperature(self):
        """Die temperature in deg C."""
        self.i2c.readfrom_mem_into(self.address, _OUT_TEMP_L, self._b2)
        raw = struct.unpack("<h", self._b2)[0]
        return 25.0 + raw / 256.0

    def calibrate_gyro(self, samples=200, delay_ms=5):
        """Average gyro readings while the sensor is held STILL; stores the bias."""
        self._gyro_bias = (0.0, 0.0, 0.0)
        sx = sy = sz = 0.0
        for _ in range(samples):
            x, y, z = self.gyro_dps
            sx += x
            sy += y
            sz += z
            time.sleep_ms(delay_ms)
        self._gyro_bias = (sx / samples, sy / samples, sz / samples)
        return self._gyro_bias
