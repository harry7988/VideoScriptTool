#!/usr/bin/env python3
"""
VideoScript Transcription Script
Uses MLX-Audio for speech recognition with GPU acceleration on Apple Silicon
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

def format_text_with_paragraphs(text):
    """
    Format text with line breaks at natural pause points.
    Creates paragraphs at sentence boundaries.
    """
    if not text:
        return text

    # Normalize whitespace
    text = re.sub(r'\s+', ' ', text).strip()

    # Split on sentence-ending punctuation (Chinese and English)
    # Pattern matches: sentence content followed by sentence-ending punctuation
    pattern = r'([^。！？.!?]+[。！？.!?])'

    parts = re.findall(pattern, text)

    if not parts:
        return text

    # Group sentences into paragraphs (2-3 sentences per paragraph)
    paragraphs = []
    current_para = []
    char_count = 0

    for sentence in parts:
        current_para.append(sentence)
        char_count += len(sentence)

        # Create new paragraph after ~150 chars or 2+ sentences
        if char_count >= 150 or len(current_para) >= 2:
            paragraphs.append(''.join(current_para))
            current_para = []
            char_count = 0

    # Add remaining sentences
    if current_para:
        paragraphs.append(''.join(current_para))

    return '\n'.join(paragraphs)

def main():
    parser = argparse.ArgumentParser(description='Transcribe audio/video files using MLX-Audio')
    parser.add_argument('--audio', required=True, help='Path to the audio file')
    parser.add_argument('--output', required=True, help='Path to the output text file')
    parser.add_argument('--model', required=True, help='Path to the MLX model')
    parser.add_argument('--language', default=None, help='Language code (e.g., en, zh)')
    parser.add_argument('--verbose', action='store_true', help='Enable verbose output')
    parser.add_argument('--no-format', action='store_true', help='Disable text formatting')

    args = parser.parse_args()

    try:
        # Import mlx_audio
        from mlx_audio.stt import load
        from mlx_audio.stt.utils import load_audio

        # Verify MLX is using GPU (Apple Silicon)
        if args.verbose:
            try:
                import mlx.core as mx
                print(f"MLX backend: {mx.default_device()}")
            except:
                pass
            print(f"Loading model from: {args.model}")

        # Load the model
        model = load(args.model)

        if args.verbose:
            print(f"Processing audio: {args.audio}")

        # Load audio file
        audio = load_audio(args.audio)

        if args.verbose:
            print("Starting transcription...")

        # Perform transcription
        # MLX automatically uses Apple Silicon GPU
        result = model.generate(audio)

        # Extract text from result
        if isinstance(result, dict):
            text = result.get('text', '')
        elif hasattr(result, 'text'):
            text = result.text
        elif isinstance(result, str):
            text = result
        elif hasattr(result, '__iter__'):
            # Handle list/generator results
            text_parts = []
            for segment in result:
                if hasattr(segment, 'text'):
                    text_parts.append(segment.text)
                elif isinstance(segment, dict):
                    text_parts.append(segment.get('text', ''))
                elif isinstance(segment, str):
                    text_parts.append(segment)
            text = ' '.join(text_parts)
        else:
            text = str(result)

        # Clean up the text
        text = text.strip()

        # Format text with paragraphs (unless --no-format is specified)
        if not args.no_format and text:
            formatted_text = format_text_with_paragraphs(text)
        else:
            formatted_text = text

        # Write to output file
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(formatted_text, encoding='utf-8')

        print(f"Transcription saved to: {args.output}")

        # Also print the text to stdout
        print("\n--- Transcription ---")
        print(formatted_text)
        print("--- End ---\n")

        return 0

    except ImportError as e:
        print(f"Error: mlx-audio not installed or import error. {e}", file=sys.stderr)
        return 1
    except Exception as e:
        print(f"Error during transcription: {e}", file=sys.stderr)
        if args.verbose:
            import traceback
            traceback.print_exc()
        return 1

if __name__ == '__main__':
    sys.exit(main())
