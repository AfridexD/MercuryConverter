using Mercury.Converter.Core;
using Xunit;

namespace Mercury.Converter.Core.Tests;

public class QualityMapperTests
{
    [Fact]
    public void Clamp_keeps_values_in_range()
    {
        Assert.Equal(QualityMapper.Lowest, QualityMapper.Clamp(-50));
        Assert.Equal(QualityMapper.Highest, QualityMapper.Clamp(500));
        Assert.Equal(60, QualityMapper.Clamp(60));
    }

    [Fact]
    public void JpegXl_is_lossless_at_100_and_visually_lossless_near_90()
    {
        Assert.Equal(0.0, QualityMapper.JxlDistance(100));
        Assert.Equal(1.0, QualityMapper.JxlDistance(90), precision: 6);
    }

    [Fact]
    public void JpegXl_distance_never_exceeds_15()
    {
        Assert.Equal(9.9, QualityMapper.JxlDistance(1), precision: 6);
        Assert.True(QualityMapper.JxlDistance(1) <= 15.0);
    }

    [Fact]
    public void H264_crf_gets_better_as_quality_rises()
    {
        Assert.Equal(15, QualityMapper.H264Crf(100));
        Assert.Equal(33, QualityMapper.H264Crf(1));
        Assert.True(QualityMapper.H264Crf(90) < QualityMapper.H264Crf(40));
    }

    [Fact]
    public void Vp9_crf_ranges_from_30_to_50()
    {
        Assert.Equal(30, QualityMapper.Vp9Crf(100));
        Assert.Equal(50, QualityMapper.Vp9Crf(1));
    }

    [Fact]
    public void Gif_palette_ranges_from_about_18_to_256_colors()
    {
        Assert.Equal(256, QualityMapper.GifColors(100));
        Assert.True(QualityMapper.GifColors(1) is >= 16 and <= 20);
    }

    [Fact]
    public void Mp3_vbr_scale_is_0_best_and_9_smallest()
    {
        Assert.Equal(0, QualityMapper.Mp3VbrScale(100));
        Assert.Equal(9, QualityMapper.Mp3VbrScale(1));
    }
}
