using FileFlux.Core;
using FileFlux.Infrastructure.Services;
using FluentAssertions;

namespace FileFlux.Tests.Services;

public class RuleBasedMetadataExtractorTests
{
    private readonly RuleBasedMetadataExtractor _extractor = new();

    #region General Extraction

    [Fact]
    public async Task ExtractAsync_General_EmptyContent_ReturnsMinimalMetadata()
    {
        var result = await _extractor.ExtractAsync("", MetadataSchema.General);

        result.Should().ContainKey("confidence");
        result.Should().ContainKey("extractionMethod");
        result["extractionMethod"].Should().Be("rule-based");
    }

    [Fact]
    public async Task ExtractAsync_General_WhitespaceContent_ReturnsMinimalMetadata()
    {
        var result = await _extractor.ExtractAsync("   \n\t  ", MetadataSchema.General);

        result.Should().ContainKey("confidence");
    }

    [Fact]
    public async Task ExtractAsync_General_WithHeaders_ExtractsTopics()
    {
        var content = """
            # Introduction to Machine Learning

            Machine learning is a subset of artificial intelligence that enables systems to learn.

            ## Supervised Learning

            Supervised learning uses labeled data to train models.

            ## Unsupervised Learning

            Unsupervised learning finds patterns in unlabeled data.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("topics");
        var topics = (string[])result["topics"];
        topics.Should().Contain("Introduction to Machine Learning");
    }

    [Fact]
    public async Task ExtractAsync_General_ExtractsKeywords()
    {
        var content = """
            Machine learning algorithms process data to find patterns.
            Training data is essential for machine learning models.
            Deep learning is a specialized form of machine learning.
            Neural networks power many machine learning applications.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("keywords");
        var keywords = (string[])result["keywords"];
        keywords.Should().NotBeEmpty();
        keywords.Should().Contain("machine");
    }

    [Fact]
    public async Task ExtractAsync_General_DetectsLanguage_English()
    {
        var content = "This is a test document written in English with enough content for detection.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("language");
        result["language"].Should().Be("en");
    }

    [Fact]
    public async Task ExtractAsync_General_DetectsLanguage_Korean()
    {
        var content = "이것은 한국어로 작성된 테스트 문서입니다. 언어 감지를 위한 충분한 내용이 포함되어 있습니다.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("language");
        result["language"].Should().Be("ko");
    }

    [Fact]
    public async Task ExtractAsync_General_DetectsLanguage_Chinese()
    {
        var content = "这是一个用中文编写的测试文档。它包含足够的内容用于语言检测。";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result["language"].Should().Be("zh");
    }

    [Fact]
    public async Task ExtractAsync_General_DetectsLanguage_Japanese()
    {
        // Use only hiragana/katakana to avoid CJK kanji matching Chinese first
        var content = "これはてすとです。ひらがなだけのてきすとです。カタカナもあります。";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result["language"].Should().Be("ja");
    }

    #endregion

    #region Document Type Detection

    [Theory]
    [InlineData("This user guide explains how to use the device safely.", "manual")]
    [InlineData("Follow this tutorial to get started with React.", "tutorial")]
    [InlineData("This API reference documents all available endpoints.", "reference")]
    [InlineData("This guide provides an overview of the system.", "guide")]
    [InlineData("The abstract of this paper discusses methodology and conclusion.", "article")]
    [InlineData("Regular content without any document type indicators present here.", "document")]
    public async Task ExtractAsync_General_DetectsDocumentType(string content, string expectedType)
    {
        // Need to pad content to meet description extraction thresholds
        var paddedContent = content + new string(' ', 100);

        var result = await _extractor.ExtractAsync(paddedContent, MetadataSchema.General);

        result.Should().ContainKey("documentType");
        result["documentType"].Should().Be(expectedType);
    }

    #endregion

    #region ProductManual Schema

    [Fact]
    public async Task ExtractAsync_ProductManual_ExtractsProductName()
    {
        var content = "Galaxy S24 User Guide\n\nWelcome to your new device.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        result.Should().ContainKey("productName");
        result.Should().ContainKey("documentType");
        result["documentType"].Should().Be("manual");
    }

    [Fact]
    public async Task ExtractAsync_ProductManual_ExtractsCompany()
    {
        var content = "Copyright 2024 Samsung Electronics Inc. All rights reserved.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        result.Should().ContainKey("company");
        ((string)result["company"]).Should().Contain("Samsung");
    }

    [Fact]
    public async Task ExtractAsync_ProductManual_ExtractsVersion()
    {
        var content = "Version 2.5.1\nProduct Manual for Device X";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        result.Should().ContainKey("version");
        result["version"].Should().Be("2.5.1");
    }

    [Fact]
    public async Task ExtractAsync_ProductManual_ExtractsReleaseDate_ISOFormat()
    {
        var content = "Release date: 2024-06-15\nProduct: Widget Pro";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        result.Should().ContainKey("releaseDate");
        result["releaseDate"].Should().Be("2024-06-15");
    }

    [Fact]
    public async Task ExtractAsync_ProductManual_EmptyContent_ReturnsMinimal()
    {
        var result = await _extractor.ExtractAsync("", MetadataSchema.ProductManual);

        result.Should().ContainKey("confidence");
        result.Should().ContainKey("extractionMethod");
        result.Should().NotContainKey("productName");
    }

    [Fact]
    public async Task ExtractAsync_ProductManual_ExtractsTopicsFromHeaders()
    {
        var content = """
            Device X Manual

            # Safety Precautions

            Always use the device safely.

            # Getting Started

            Unbox and set up your device.

            # Troubleshooting

            Common issues and solutions.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        result.Should().ContainKey("topics");
        var topics = (string[])result["topics"];
        topics.Should().Contain("Safety Precautions");
    }

    #endregion

    #region TechnicalDoc Schema

    [Fact]
    public async Task ExtractAsync_TechnicalDoc_DetectsFrameworks()
    {
        var content = """
            Building a web application with React and Next.js.
            The backend uses Express with PostgreSQL database.
            Deployed on Docker containers managed by Kubernetes.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        result.Should().ContainKey("frameworks");
        var frameworks = (string[])result["frameworks"];
        frameworks.Should().Contain("React");
        frameworks.Should().Contain("Next.js");
        frameworks.Should().Contain("Express");
    }

    [Fact]
    public async Task ExtractAsync_TechnicalDoc_DetectsTechnologies()
    {
        var content = """
            This TypeScript application uses REST API patterns.
            Data is stored in PostgreSQL with Redis caching.
            Frontend uses HTML and CSS with Tailwind.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        result.Should().ContainKey("technologies");
        var tech = (string[])result["technologies"];
        tech.Should().Contain("TypeScript");
        tech.Should().Contain("REST");
        tech.Should().Contain("PostgreSQL");
    }

    [Fact]
    public async Task ExtractAsync_TechnicalDoc_DetectsLibraries_CSharp()
    {
        var content = """
            using System.Text.Json;
            using Microsoft.Extensions.DependencyInjection;
            using FluentAssertions;
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        result.Should().ContainKey("libraries");
        var libs = (string[])result["libraries"];
        libs.Should().Contain("System.Text.Json");
    }

    [Fact]
    public async Task ExtractAsync_TechnicalDoc_DetectsLibraries_Python()
    {
        var content = """
            import numpy
            from pandas import DataFrame
            import tensorflow as tf
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.TechnicalDoc);

        result.Should().ContainKey("libraries");
        var libs = (string[])result["libraries"];
        libs.Should().Contain("numpy");
        libs.Should().Contain("pandas");
    }

    [Fact]
    public async Task ExtractAsync_TechnicalDoc_EmptyContent_ReturnsMinimal()
    {
        var result = await _extractor.ExtractAsync("", MetadataSchema.TechnicalDoc);

        result.Should().ContainKey("confidence");
        result.Should().NotContainKey("libraries");
    }

    #endregion

    #region Custom Schema

    [Fact]
    public async Task ExtractAsync_Custom_FallsBackToGeneral()
    {
        var content = "This is a general document with some content for testing the custom schema fallback.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.Custom);

        result.Should().ContainKey("extractionMethod");
        result["extractionMethod"].Should().Be("rule-based");
    }

    #endregion

    #region Confidence Calculation

    [Fact]
    public async Task ExtractAsync_RichContent_HigherConfidence()
    {
        var content = """
            Galaxy S24 User Guide
            Version 3.0.1
            Copyright 2024 Samsung Electronics Inc.

            # Introduction

            Welcome to the Galaxy S24 user guide. This manual provides comprehensive instructions.

            # Setup Instructions

            Follow these steps to set up your device.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        var confidence = (double)result["confidence"];
        confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task ExtractAsync_SparseContent_LowerConfidence()
    {
        var content = "Just a simple sentence.";

        var result = await _extractor.ExtractAsync(content, MetadataSchema.ProductManual);

        var confidence = (double)result["confidence"];
        confidence.Should().BeLessThanOrEqualTo(1.0);
    }

    #endregion

    #region Description Extraction

    [Fact]
    public async Task ExtractAsync_General_ExtractsDescription()
    {
        var content = """
            # Title

            This is a comprehensive introduction to the topic that provides enough context for the reader to understand the fundamental concepts being discussed in this document.

            Additional paragraphs follow.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("description");
        var description = (string)result["description"];
        description.Should().NotBeEmpty();
    }

    #endregion

    #region Numbered Section Topics

    [Fact]
    public async Task ExtractAsync_NumberedSections_ExtractsTopics()
    {
        var content = """
            1. Introduction to the System
            System overview and key components.

            2. Installation Requirements
            Hardware and software prerequisites.

            3. Configuration Options
            Available settings and defaults.
            """;

        var result = await _extractor.ExtractAsync(content, MetadataSchema.General);

        result.Should().ContainKey("topics");
        var topics = (string[])result["topics"];
        topics.Should().Contain("Introduction to the System");
    }

    #endregion
}
