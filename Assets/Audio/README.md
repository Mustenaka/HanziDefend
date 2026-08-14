# M1 feedback audio

`Placeholder/*.wav` is generated from `Assets/GameData/feedback.json` by:

`HanziDefend/Feedback/Regenerate Placeholder Audio`

The generated tones are temporary M1 assets. Runtime code resolves them through the single
`HanziDefendFeedbackAudioCatalog` resource; missing clips intentionally degrade to silence.
