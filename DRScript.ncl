begin
    f = addfile("/home/jr-fuentes-martinez/Documentos/2023/03/20230301120000-UKMO-L4_GHRSST-SSTfnd-OSTIA-GLOB-v02.0-fv02.0.nc","w")
    f@_Fillvalue = 0B
    print(f@_Fillvalue)
    sifn=f->sea_ice_fraction(0,{0:90},:)
    sifs=f->sea_ice_fraction(0,{-90:0},:)
    tsifn = sum( sifn(lat|:,lon|:) )
    tsifs = sum( sifs(lat|:,lon|:) )
    alist = [/tsifn, tsifs/]
    write_table("/home/jr-fuentes-martinez/Documentos/test.asc", "w", alist, "%d%d")
end
