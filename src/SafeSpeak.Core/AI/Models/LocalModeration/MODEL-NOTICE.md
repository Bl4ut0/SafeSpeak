# SafeSpeak local moderation model notice

SafeSpeak bundles `navodPeiris/minilm-toxic-classifier` for offline text
classification. No chat text is sent to Hugging Face or another moderation API.

- Source: https://huggingface.co/navodPeiris/minilm-toxic-classifier
- Pinned revision: `4831179af569756699fdd6132a520dcdbfe07f03`
- Base architecture: MiniLMv2-L6-H384, 23 million parameters
- Training data: Jigsaw Toxic Comment Classification dataset
- Model license declared by the model card: Apache-2.0
- Full license text: `LICENSE.apache-2.0.txt`
- ONNX model SHA-256: `935BA953C9D4478D809DB1A2FA40181F42BF1670D1E69261478B2137C1FBACC5`
- Tokenizer SHA-256: `851CA67100D372CA3AE031A6ABD168F53489EEBFD7D89523F35C5C9B4D372C3C`

The model provides six scores: toxic, severe toxicity, obscene language,
threat, insult, and identity hate. SafeSpeak combines these scores with its
deterministic banned-term and anti-evasion rules. The model is an aid, not a
guarantee: it was trained primarily on English comments and may produce false
positives or false negatives, especially for short, novel, or reclaimed text.
