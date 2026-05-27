param(
    [string] $CoreProtoRoot = 'C:/TKLobby/uengine/uframework/Assets/JJService/tkgameservice/Editor/Resource/Proto',
    [string] $LordUnionProtoRoot = 'C:/TKLobby/uengine/uframework/Assets/JJGame/lordunion/Editor/Resource/Proto',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'Generated')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ProtoPath {
    param(
        [Parameter(Mandatory = $true)] [string] $Root,
        [Parameter(Mandatory = $true)] [string] $RelativePath
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Proto source not found: $path"
    }

    return (Resolve-Path -LiteralPath $path).Path
}

function Remove-ProtoComments {
    param([Parameter(Mandatory = $true)] [string] $Text)

    $withoutBlockComments = [regex]::Replace(
        $Text,
        '/\*.*?\*/',
        '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    return [regex]::Replace($withoutBlockComments, '(?m)//.*$', '')
}

function Get-ProtoBlocks {
    param(
        [Parameter(Mandatory = $true)] [string] $Text,
        [Parameter(Mandatory = $true)] [string] $Kind
    )

    $pattern = "(?m)^\s*$Kind\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{"
    $coveredUntil = -1
    foreach ($match in [regex]::Matches($Text, $pattern)) {
        if ($match.Index -le $coveredUntil) {
            continue
        }

        $name = $match.Groups[1].Value
        $openBrace = $match.Index + $match.Value.LastIndexOf('{')
        $depth = 0
        $closeBrace = -1

        for ($i = $openBrace; $i -lt $Text.Length; $i++) {
            $charCode = [int][char]$Text[$i]
            if ($charCode -eq 123) {
                $depth++
            }
            elseif ($charCode -eq 125) {
                $depth--
                if ($depth -eq 0) {
                    $closeBrace = $i
                    break
                }
            }
        }

        if ($closeBrace -lt 0) {
            throw "Unclosed $Kind block: $name"
        }

        [pscustomobject]@{
            Name = $name
            Body = $Text.Substring($openBrace + 1, $closeBrace - $openBrace - 1)
        }
        $coveredUntil = $closeBrace
    }
}

function Remove-NestedProtoBlocks {
    param(
        [Parameter(Mandatory = $true)] [string] $Body,
        [Parameter(Mandatory = $true)] [string] $SourceFile,
        [Parameter(Mandatory = $true)] [string] $MessageName
    )

    $ranges = New-Object System.Collections.Generic.List[object]
    $pattern = '(?m)^\s*(message|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{'
    $coveredUntil = -1

    foreach ($match in [regex]::Matches($Body, $pattern)) {
        if ($match.Index -le $coveredUntil) {
            continue
        }

        $nestedName = $match.Groups[2].Value
        $openBrace = $match.Index + $match.Value.LastIndexOf('{')
        $depth = 0
        $closeBrace = -1

        for ($i = $openBrace; $i -lt $Body.Length; $i++) {
            $charCode = [int][char]$Body[$i]
            if ($charCode -eq 123) {
                $depth++
            }
            elseif ($charCode -eq 125) {
                $depth--
                if ($depth -eq 0) {
                    $closeBrace = $i
                    break
                }
            }
        }

        if ($closeBrace -lt 0) {
            throw "Unclosed nested proto block '$nestedName' in $SourceFile message $MessageName"
        }

        $ranges.Add([pscustomobject]@{
            Start = $match.Index
            Length = $closeBrace - $match.Index + 1
        })
        $coveredUntil = $closeBrace
    }

    $result = $Body
    foreach ($range in ($ranges | Sort-Object Start -Descending)) {
        $result = $result.Remove($range.Start, $range.Length)
    }

    return $result
}

function ConvertTo-CSharpIdentifier {
    param([Parameter(Mandatory = $true)] [string] $Name)

    $parts = @($Name -split '_+' | Where-Object { $_.Length -gt 0 })
    if ($parts.Count -eq 0) {
        return 'Value'
    }

    $converted = foreach ($part in $parts) {
        if ($part.Length -eq 1) {
            $part.ToUpperInvariant()
        }
        else {
            $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1)
        }
    }

    $identifier = [string]::Join('', $converted)
    if ($identifier -match '^[0-9]') {
        return "Value$identifier"
    }

    return $identifier
}

function ConvertTo-CSharpTypeName {
    param(
        [AllowEmptyCollection()] [string[]] $Scope,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $parts = @()
    foreach ($scopePart in $Scope) {
        $parts += ConvertTo-CSharpIdentifier $scopePart
    }
    $parts += ConvertTo-CSharpIdentifier $Name

    return [string]::Join('', $parts)
}

function Get-ProtoPackageScope {
    param([Parameter(Mandatory = $true)] [string] $Text)

    $match = [regex]::Match($Text, '(?m)^\s*package\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;')
    if (-not $match.Success) {
        return @()
    }

    $parts = @($match.Groups[1].Value -split '\.' | Where-Object { $_.Length -gt 0 })
    if ($parts.Count -eq 0 -or $parts[0] -ne 'Protocol') {
        return @()
    }

    return @($parts | Where-Object { $_ -ne 'Protocol' })
}

function Get-ProtoDefinitions {
    param(
        [Parameter(Mandatory = $true)] [string] $Text,
        [Parameter(Mandatory = $true)] [string] $Kind,
        [AllowEmptyCollection()] [string[]] $PackageScope,
        [AllowEmptyCollection()] [string[]] $Scope,
        [Parameter(Mandatory = $true)] [string] $SourceFile
    )

    foreach ($block in Get-ProtoBlocks $Text $Kind) {
        $fullNameParts = @($PackageScope) + @($Scope) + $block.Name
        [pscustomobject]@{
            Name = $block.Name
            PackageScope = @($PackageScope)
            Scope = @($Scope)
            FullName = [string]::Join('.', $fullNameParts)
            CSharpName = ConvertTo-CSharpTypeName $Scope $block.Name
            Body = $block.Body
            SourceFile = $SourceFile
        }

        if ($Kind -eq 'message') {
            $nestedScope = @($Scope) + $block.Name
            foreach ($nestedMessage in Get-ProtoDefinitions $block.Body 'message' $PackageScope $nestedScope $SourceFile) {
                $nestedMessage
            }

            foreach ($nestedEnum in Get-ProtoDefinitions $block.Body 'enum' $PackageScope $nestedScope $SourceFile) {
                $nestedEnum
            }
        }
    }
}

function Add-ProtoType {
    param(
        [Parameter(Mandatory = $true)] [System.Collections.Specialized.OrderedDictionary] $TypesByFullName,
        [Parameter(Mandatory = $true)] [hashtable] $TypesBySimpleName,
        [Parameter(Mandatory = $true)] [object] $Type
    )

    if ($TypesByFullName.Contains($Type.FullName)) {
        throw "Duplicate proto type '$($Type.FullName)' in $($TypesByFullName[$Type.FullName].SourceFile) and $($Type.SourceFile)"
    }

    $TypesByFullName[$Type.FullName] = $Type
    if (-not $TypesBySimpleName.ContainsKey($Type.Name)) {
        $TypesBySimpleName[$Type.Name] = New-Object System.Collections.Generic.List[object]
    }

    $TypesBySimpleName[$Type.Name].Add($Type)
}

function Resolve-ProtoType {
    param(
        [Parameter(Mandatory = $true)] [string] $ProtoType,
        [Parameter(Mandatory = $true)] [object] $DeclaringMessage,
        [Parameter(Mandatory = $true)] [string] $FieldName
    )

    $typeName = $ProtoType
    if ($typeName.StartsWith('.')) {
        $typeName = $typeName.Substring(1)
    }

    $typeNameParts = @($typeName -split '\.' | Where-Object { $_.Length -gt 0 })
    $simpleName = $typeNameParts[-1]

    if ($typeNameParts.Count -gt 1) {
        for ($start = 0; $start -lt $typeNameParts.Count; $start++) {
            $candidate = [string]::Join('.', $typeNameParts[$start..($typeNameParts.Count - 1)])
            if ($script:TypesByFullName.Contains($candidate)) {
                return $script:TypesByFullName[$candidate].CSharpName
            }
        }
    }
    else {
        $packageScope = @($DeclaringMessage.PackageScope)
        $scope = @($DeclaringMessage.Scope)
        $nestedCandidateParts = @($packageScope) + @($scope) + $DeclaringMessage.Name + $simpleName
        $nestedCandidate = [string]::Join('.', $nestedCandidateParts)
        if ($script:TypesByFullName.Contains($nestedCandidate)) {
            return $script:TypesByFullName[$nestedCandidate].CSharpName
        }

        for ($count = $scope.Count; $count -ge 0; $count--) {
            $candidateParts = @($packageScope)
            if ($count -gt 0) {
                $candidateParts += $scope[0..($count - 1)]
            }
            $candidateParts += $simpleName
            $candidate = [string]::Join('.', $candidateParts)
            if ($script:TypesByFullName.Contains($candidate)) {
                return $script:TypesByFullName[$candidate].CSharpName
            }
        }
    }

    if (-not $script:TypesBySimpleName.ContainsKey($simpleName)) {
        throw "Unknown proto type '$ProtoType' in $($DeclaringMessage.SourceFile) message $($DeclaringMessage.FullName) field $FieldName"
    }

    $candidates = $script:TypesBySimpleName[$simpleName]
    if ($candidates.Count -eq 1) {
        return $candidates[0].CSharpName
    }

    $locations = [string]::Join(', ', ($candidates | ForEach-Object { "$($_.FullName) in $($_.SourceFile)" }))
    throw "Ambiguous proto type '$ProtoType' in $($DeclaringMessage.SourceFile) message $($DeclaringMessage.FullName) field $FieldName. Candidates: $locations"
}

function Disambiguate-CSharpTypeNames {
    param([Parameter(Mandatory = $true)] [object[]] $Types)

    $groups = $Types | Group-Object CSharpName | Where-Object { $_.Count -gt 1 }
    foreach ($group in $groups) {
        foreach ($type in $group.Group) {
            if ($type.PackageScope.Count -gt 0) {
                $type.CSharpName = ConvertTo-CSharpTypeName $type.PackageScope $type.CSharpName
            }
        }
    }
}

function ConvertTo-CSharpType {
    param(
        [Parameter(Mandatory = $true)] [string] $ProtoType,
        [Parameter(Mandatory = $true)] [bool] $IsRepeated,
        [Parameter(Mandatory = $true)] [object] $DeclaringMessage,
        [Parameter(Mandatory = $true)] [string] $FieldName
    )

    $typeName = $ProtoType
    if ($typeName.Contains('.')) {
        $typeName = ($typeName -split '\.')[-1]
    }

    $mappedType = switch ($typeName) {
        'double' { 'double'; break }
        'float' { 'float'; break }
        'int32' { 'int'; break }
        'sint32' { 'int'; break }
        'sfixed32' { 'int'; break }
        'int64' { 'long'; break }
        'sint64' { 'long'; break }
        'sfixed64' { 'long'; break }
        'uint32' { 'uint'; break }
        'fixed32' { 'uint'; break }
        'uint64' { 'ulong'; break }
        'fixed64' { 'ulong'; break }
        'bool' { 'bool'; break }
        'string' { 'string'; break }
        'bytes' { 'byte[]'; break }
        default { Resolve-ProtoType $ProtoType $DeclaringMessage $FieldName; break }
    }

    if ($IsRepeated) {
        return "List<$mappedType>"
    }

    return $mappedType
}

function Test-ReferenceType {
    param([Parameter(Mandatory = $true)] [string] $CSharpType)

    return $CSharpType -eq 'string' -or
        $CSharpType -eq 'byte[]' -or
        ($CSharpType -notin @('double', 'float', 'int', 'long', 'uint', 'ulong', 'bool'))
}

function Get-ProtoFields {
    param(
        [Parameter(Mandatory = $true)] [object] $Message
    )

    $sourceFile = if ($Message.PSObject.Properties.Name -contains 'SourceFile') { $Message.SourceFile } else { '<unknown>' }
    $fieldBody = Remove-NestedProtoBlocks $Message.Body $sourceFile $Message.Name
    $usedFieldNumbers = @{}

    foreach ($match in [regex]::Matches($fieldBody, $script:FieldPattern)) {
        $fieldNumber = $match.Groups[4].Value
        if ($usedFieldNumbers.ContainsKey($fieldNumber)) {
            $messageName = if ($Message.PSObject.Properties.Name -contains 'FullName') { $Message.FullName } else { $Message.Name }
            throw "Duplicate proto field number $fieldNumber in $sourceFile message $messageName"
        }

        $usedFieldNumbers[$fieldNumber] = $true
        [pscustomobject]@{
            Label = $match.Groups[1].Value
            Type = $match.Groups[2].Value
            Name = $match.Groups[3].Value
            Number = $fieldNumber
            SourceFile = $sourceFile
            MessageName = $Message.Name
        }
    }
}

$protoFiles = @(
    Resolve-ProtoPath $CoreProtoRoot 'TKMobile.proto'
    Resolve-ProtoPath $CoreProtoRoot 'TKLobby.proto'
    Resolve-ProtoPath $CoreProtoRoot 'TKMatch.proto'
    Resolve-ProtoPath $CoreProtoRoot 'TKPartnerRoom.proto'
    Resolve-ProtoPath $CoreProtoRoot 'Partnerroom/TKDefine.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKMobileLordUnion.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKHLLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKLZLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKSDLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKHZLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKDDLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKDBLord.proto'
    Resolve-ProtoPath $LordUnionProtoRoot 'TKDJLord.proto'
)

$allEnums = [ordered]@{}
$allMessages = [ordered]@{}
$script:TypesByFullName = [ordered]@{}
$script:TypesBySimpleName = @{}
$script:FieldPattern = '(?m)^\s*(optional|required|repeated)\s+([.A-Za-z_][.A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([0-9]+)'
$mobileWrapperFields = @{
    TKMobileReqMsg = [ordered]@{}
    TKMobileAckMsg = [ordered]@{}
}
$mobileWrapperAllowedFields = @{
    TKMobileReqMsg = @(
        'param'
        'lobby_req_msg'
        'match_req_msg'
        'lord_req_msg'
        'hllord_req_msg'
        'lzlord_req_msg'
        'sdlord_req_msg'
        'hzlord_req_msg'
        'ddlord_req_msg'
        'dblord_req_msg'
        'djlord_req_msg'
    )
    TKMobileAckMsg = @(
        'param'
        'lobby_ack_msg'
        'match_ack_msg'
        'lord_ack_msg'
        'hllord_ack_msg'
        'lzlord_ack_msg'
        'sdlord_ack_msg'
        'hzlord_ack_msg'
        'ddlord_ack_msg'
        'dblord_ack_msg'
        'djlord_ack_msg'
    )
}

foreach ($protoFile in $protoFiles) {
    $text = Remove-ProtoComments ([System.IO.File]::ReadAllText($protoFile))
    $packageScope = @(Get-ProtoPackageScope $text)

    foreach ($enum in Get-ProtoDefinitions $text 'enum' $packageScope @() $protoFile) {
        Add-ProtoType $allEnums $script:TypesBySimpleName $enum
        $script:TypesByFullName[$enum.FullName] = $enum
    }

    foreach ($message in Get-ProtoDefinitions $text 'message' $packageScope @() $protoFile) {
        if ($mobileWrapperFields.ContainsKey($message.Name)) {
            foreach ($field in Get-ProtoFields $message) {
                $fieldName = $field.Name
                if ($mobileWrapperAllowedFields[$message.Name] -contains $fieldName) {
                    $fieldNumber = $field.Number
                    if (-not $mobileWrapperFields[$message.Name].Contains($fieldNumber)) {
                        $mobileWrapperFields[$message.Name][$fieldNumber] = [pscustomobject]@{
                            Label = $field.Label
                            Type = $field.Type
                            Name = $fieldName
                            Number = $fieldNumber
                            SourceFile = $field.SourceFile
                        }
                    }
                }
            }
        }

        if ($message.FullName -in @('TKMobileReqMsg', 'TKMobileAckMsg')) {
            continue
        }

        Add-ProtoType $allMessages $script:TypesBySimpleName $message
        $script:TypesByFullName[$message.FullName] = $message
    }
}

foreach ($wrapperName in $mobileWrapperFields.Keys) {
    $body = [System.Text.StringBuilder]::new()
    foreach ($field in $mobileWrapperFields[$wrapperName].Values | Sort-Object { [int]$_.Number }) {
        [void] $body.AppendLine("    $($field.Label) $($field.Type) $($field.Name) = $($field.Number);")
    }

    $wrapperMessage = [pscustomobject]@{
        Name = $wrapperName
        PackageScope = @()
        Scope = @()
        FullName = $wrapperName
        CSharpName = $wrapperName
        Body = $body.ToString()
        SourceFile = 'TKMobile.proto + TKMobileLordUnion.proto'
    }
    Add-ProtoType $allMessages $script:TypesBySimpleName $wrapperMessage
    $script:TypesByFullName[$wrapperMessage.FullName] = $wrapperMessage
}

$requiredMessages = @(
    'TKMobileReqMsg'
    'TKMobileAckMsg'
    'LordReqMsg'
    'LordAckMsg'
)

foreach ($requiredMessage in $requiredMessages) {
    if (-not $allMessages.Contains($requiredMessage)) {
        throw "Required proto message was not generated: $requiredMessage"
    }
}

foreach ($message in $allMessages.Values) {
    [void] @(Get-ProtoFields $message)
}

Disambiguate-CSharpTypeNames (@($allEnums.Values) + @($allMessages.Values))

$usedCSharpTypeNames = @{}
foreach ($type in @($allEnums.Values) + @($allMessages.Values)) {
    if ($usedCSharpTypeNames.ContainsKey($type.CSharpName)) {
        throw "Duplicate generated C# type name '$($type.CSharpName)' for $($usedCSharpTypeNames[$type.CSharpName].FullName) and $($type.FullName)"
    }

    $usedCSharpTypeNames[$type.CSharpName] = $type
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = Join-Path $OutputDirectory 'LordUnionProtocol.g.cs'
$builder = [System.Text.StringBuilder]::new()

[void] $builder.AppendLine('// <auto-generated />')
[void] $builder.AppendLine('#nullable enable')
[void] $builder.AppendLine('using ProtoBuf;')
[void] $builder.AppendLine('using System.Collections.Generic;')
[void] $builder.AppendLine()
[void] $builder.AppendLine('namespace LordUnion.IntegrationTests.Protocol.Generated;')
[void] $builder.AppendLine()

foreach ($enum in $allEnums.Values) {
    [void] $builder.AppendLine("public enum $($enum.CSharpName)")
    [void] $builder.AppendLine('{')

    foreach ($match in [regex]::Matches($enum.Body, '(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?[0-9]+)')) {
        [void] $builder.AppendLine("    $($match.Groups[1].Value) = $($match.Groups[2].Value),")
    }

    [void] $builder.AppendLine('}')
    [void] $builder.AppendLine()
}

foreach ($message in $allMessages.Values) {
    [void] $builder.AppendLine('[ProtoContract]')
    [void] $builder.AppendLine("public sealed partial class $($message.CSharpName)")
    [void] $builder.AppendLine('{')

    $usedPropertyNames = @{}
    foreach ($field in Get-ProtoFields $message) {
        $label = $field.Label
        $protoType = $field.Type
        $fieldName = $field.Name
        $fieldNumber = $field.Number
        $isRepeated = $label -eq 'repeated'
        $sourceFile = $field.SourceFile
        $csharpType = ConvertTo-CSharpType $protoType $isRepeated $message $fieldName
        $basePropertyName = ConvertTo-CSharpIdentifier $fieldName
        $propertyName = $basePropertyName
        $collisionIndex = 0
        while ($usedPropertyNames.ContainsKey($propertyName)) {
            $collisionIndex++
            $propertyName = "${basePropertyName}${fieldNumber}"
            if ($collisionIndex -gt 1) {
                $propertyName = "${propertyName}_$collisionIndex"
            }
        }
        $usedPropertyNames[$propertyName] = $true

        if ($label -eq 'required') {
            [void] $builder.AppendLine("    [ProtoMember($fieldNumber, IsRequired = true)]")
        }
        else {
            [void] $builder.AppendLine("    [ProtoMember($fieldNumber)]")
        }

        if ($isRepeated) {
            [void] $builder.AppendLine("    public $csharpType $propertyName { get; } = new();")
        }
        elseif ($label -eq 'required' -and (Test-ReferenceType $csharpType)) {
            $initializer = if ($csharpType -eq 'string') {
                'string.Empty'
            }
            elseif ($csharpType -eq 'byte[]') {
                'System.Array.Empty<byte>()'
            }
            else {
                'new()'
            }

            [void] $builder.AppendLine("    public $csharpType $propertyName { get; set; } = $initializer;")
        }
        elseif ($label -eq 'optional' -and (Test-ReferenceType $csharpType)) {
            [void] $builder.AppendLine("    public ${csharpType}? $propertyName { get; set; }")
        }
        else {
            [void] $builder.AppendLine("    public $csharpType $propertyName { get; set; }")
        }

        [void] $builder.AppendLine()
    }

    [void] $builder.AppendLine('}')
    [void] $builder.AppendLine()
}

Set-Content -LiteralPath $outputPath -Value $builder.ToString() -Encoding ASCII
Write-Host "Generated $outputPath from $($protoFiles.Count) proto files."
