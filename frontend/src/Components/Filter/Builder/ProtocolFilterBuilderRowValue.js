import React from 'react';
import FilterBuilderRowValue from './FilterBuilderRowValue';

// Ids are the protocol type names the API now sends, not the old lowercase enum names.
const protocols = [
  { id: 'TorrentDownloadProtocol', name: 'Torrent' },
  { id: 'UsenetDownloadProtocol', name: 'Usenet' },
  { id: 'SoulseekDownloadProtocol', name: 'Soulseek' }
];

function ProtocolFilterBuilderRowValue(props) {
  return (
    <FilterBuilderRowValue
      tagList={protocols}
      {...props}
    />
  );
}

export default ProtocolFilterBuilderRowValue;
